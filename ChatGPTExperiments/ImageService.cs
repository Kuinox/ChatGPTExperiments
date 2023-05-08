using CatBox.NET;
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.Options;
using System.Net.Mail;
using System.Text.Json;
using System.Text.RegularExpressions;
using Image = SixLabors.ImageSharp.Image;

namespace KuinoxSemiAGI;

public partial class ImageService : InteractionModuleBase<SocketInteractionContext>
{
    readonly HttpClient _httpClient;
    readonly ICatBoxClient _catBox;
    readonly IOptions<ImageServiceConfig> _config;

    public ImageService( IHttpClientFactory httpClientFactory, ICatBoxClient catBox, IOptions<ImageServiceConfig> options )
    {
        _httpClient = httpClientFactory.CreateClient( "ImageService" );
        _catBox = catBox;
        _config = options;
    }

    [SlashCommand( "rotate", "Rotates an image." )]
    [RequireUserPermission( GuildPermission.SendMessages )]
    public Task RotateImage(
        [Summary( "Image", "The image to rotate." )] IAttachment image, [Summary( "Rotation", "How the image will be rotated." )] RotateOption rotateOption )
        => RotateImage( image.Url, rotateOption );

    [SlashCommand( "rotate_url", "Rotates an image." )]
    [RequireUserPermission( GuildPermission.SendMessages )]
    public async Task RotateImage(
        [Summary( "ImageUrl", "The url of the image to rotate." )] string url, [Summary( "Rotation", "How the image will be rotated." )] RotateOption rotateOption )
    {
        try
        {
            await DeferAsync();

            var response = await _httpClient.GetAsync( url );
            response.EnsureSuccessStatusCode();

            using( var imageStream = await response.Content.ReadAsStreamAsync() )
            {
                using( var originalImage = Image.Load<Rgba32>( imageStream ) )
                {
                    var originalSize = originalImage.Size;

                    // Rotate the image using SixLabors.ImageSharp's Rotate method
                    using( var rotatedImage = originalImage.Clone( x => x.Rotate(
                        rotateOption switch
                        {
                            RotateOption.Rotate90 => RotateMode.Rotate90,
                            RotateOption.Rotate180 => RotateMode.Rotate180,
                            RotateOption.Rotate270 => RotateMode.Rotate270,
                            _ => throw new InvalidOperationException(),
                        }
                    ) ) )
                    {
                        await FollowupAsync( ":arrows_counterclockwise:" );
                        var mem = new MemoryStream();
                        rotatedImage.SaveAsPng( mem );
                        mem.Position = 0;
                        await Context.Channel.SendFileAsync( new FileAttachment( mem, "image.png" ) );
                    }
                }
            }
        }
        catch( Exception ex )
        {
            await FollowupAsync( $"Error: {ex.Message}" );
        }
    }

    
    [SlashCommand( "catbox_upload", "Upload an image to catbox.moe!" )]
    public async Task CatboxUpload( IAttachment attachment )
    {
        await CatboxUpload( attachment.Url );
    }

    [SlashCommand( "catbox_upload_url", "Upload an image to catbox.moe!" )]
    public async Task CatboxUpload( string url )
    {
        var imgUrl = await _catBox.UploadMultipleUrls( new UrlUploadRequest()
        {
            Files = new[] { new Uri( url ) },
            UserHash = _config.Value.CatboxUserHash
        } ).SingleAsync();
        await RespondAsync( imgUrl );
    }

    async Task<bool> Is502( string content )
    {
        if( content.Contains( "502" ) )
        {
            await FollowupAsync( $"CatBox returned 502, stopping backup." );
            return true;
        }
        return false;
    }

    [SlashCommand( "imgur_backup", "Backup all imgur images to discord." )]
    public async Task FindImgurLinks()
    {
        await DeferAsync();
        // search for messages containing images in the current channel
        var last = await Context.Channel.GetMessagesAsync( 1 ).Flatten().SingleAsync();
        var messages = Context.Channel.GetMessagesAsync( last, Direction.Before, int.MaxValue, CacheMode.AllowDownload, new RequestOptions()
        {
            RetryMode = RetryMode.RetryRatelimit
        } ).Flatten();
        var alreadyBackupedImages = new HashSet<string>();
        int i = 0;
        // count the images per hostname
        var links = messages
            .SelectAwait( async s =>
            {
                // LINQ Hack: we use the side effect to avoid storing all the messages
                Interlocked.Increment( ref i );
                if( s.Author.Id == Context.Client.CurrentUser.Id )
                {
                    if( Uri.TryCreate( s.Content, UriKind.Absolute, out var uri ) && uri.Host.Contains( "catbox.moe" ) )
                    {
                        if( s.Reference.MessageId.IsSpecified )
                        {

                            var msg = await Context.Channel.GetMessageAsync( s.Reference.MessageId.Value );
                            var links = ImgurAPI.ExtractMsgLinks( msg )
                                .Where( s => s.Contains( "imgur" ) )
                                .Select( s => ImgurAPI.GetImgurId( s ) );
                            foreach( var link in links )
                            {
                                alreadyBackupedImages.Add( link );
                            }
                        }
                    }
                }
                return s;
            } )
            .OfType<IUserMessage>()
            .SelectMany( s => ImgurAPI.ExtractMsgLinks( s ).Select( x => (msg: s, link: x) ).ToAsyncEnumerable() )
            .Where( s => Uri.TryCreate( s.link, UriKind.Absolute, out var uri ) && (uri.Scheme == "http" || uri.Scheme == "https") )
            .Where( s =>
            {
                var host = new Uri( s.link ).Host;
                return host == "i.imgur.com" || host == "imgur.com";
            } )
            .Distinct( s => ImgurAPI.GetImgurId( s.link ) );
        int albumCount = 0;
        int albumImageCount = 0;
        int imageCount = 0;
        int alreadyProcessed = 0;
        int failCount = 0;
        int notFoundCount = 0;
        await foreach( var (msg, link) in links )
        {
            if( alreadyBackupedImages.Contains( ImgurAPI.GetImgurId( link ) ) )
            {
                alreadyProcessed += 1;
                continue;
            }
            if( ImgurAPI.IsAlbumLink( link ) )
            {
                var album = await ImgurAPI.GetAlbumData(_httpClient, link );
                if( album is null )
                {
                    notFoundCount += 1;
                    continue;
                }
                albumCount += 1;
                albumImageCount += album.media!.Length;

                var imageLinks = await _catBox.UploadMultipleUrls( new UrlUploadRequest()
                {
                    Files = album.media.Select( s => new Uri( s.url! ) ),
                    UserHash = _config.Value.CatboxUserHash
                } ).ToArrayAsync();

                var catboxLink = await _catBox.CreateAlbum( new CreateAlbumRequest()
                {
                    Description = $"Migrated from imgur album {link}",
                    Files = imageLinks!,
                    Title = album.title!,
                    UserHash = _config.Value.CatboxUserHash
                } );
                if( await Is502( catboxLink! ) ) return;
                if( !Uri.TryCreate( catboxLink, UriKind.Absolute, out _ ) )
                {
                    failCount += 1;
                }
                await msg.ReplyAsync( Uri.TryCreate( catboxLink, UriKind.Absolute, out _ ) ? $"Imgur album backed up to {catboxLink}." : catboxLink, allowedMentions: new AllowedMentions( AllowedMentionTypes.None ) );
            }
            else
            {
                imageCount += 1;
                var data = ImgurAPI.IsGalleryLink( link ) ? await ImgurAPI.GetGalleryData( _httpClient, link ) : await ImgurAPI.GetPostData( _httpClient, link );
                if( data is null )
                {
                    notFoundCount += 1;
                    continue;
                }
                string url = data!.media!.Single().url!;
                var catboxLink = await _catBox.UploadMultipleUrls( new UrlUploadRequest()
                {
                    Files = new[] { new Uri( url ) },
                    UserHash = _config.Value.CatboxUserHash
                } ).SingleAsync();
                if( await Is502( catboxLink! ) ) return;
                if( !Uri.TryCreate( catboxLink, UriKind.Absolute, out _ ) )
                {
                    failCount += 1;
                }
                await msg.ReplyAsync( catboxLink, allowedMentions: new AllowedMentions( AllowedMentionTypes.None ) );
            }
        }
        var str = $"Processed {i} messages.\n{albumCount} albums which contain a sum of {albumImageCount} images.\n{imageCount} images link.\n{failCount} fails.\n{notFoundCount} 404s.\n{alreadyProcessed} already backuped.";
        try
        {
            await FollowupAsync( str );
        }
        catch( InvalidOperationException )
        {
            await Context.Channel.SendMessageAsync( str );
        }
    }
}
