using Discord;
using Discord.Interactions;
using System.Net.Mail;
using System.Text.RegularExpressions;
using Image = SixLabors.ImageSharp.Image;

namespace KuinoxSemiAGI;

public class ImageService : InteractionModuleBase<SocketInteractionContext>
{
    readonly HttpClient _httpClient;
    public ImageService( IHttpClientFactory httpClientFactory )
    {
        _httpClient = httpClientFactory.CreateClient( "ImageService" );
    }

    public enum RotateOption
    {
        [ChoiceDisplay( "Rotate 90 degrees clockwise." )]
        Rotate90 = 90,

        [ChoiceDisplay( "Rotate 180 degrees clockwise." )]
        Rotate180 = 180,

        [ChoiceDisplay( "Rotate 270 degrees clockwise." )]
        Rotate270 = 270
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

    static readonly Regex _htmlRegex = new( "https:\\/\\/s\\.imgur\\.com\\/desktop-assets\\/js\\/main\\.[a-f\\d]+\\.js" );
    static readonly Regex _clientRegex = new( "\"([^\\\"]*)\\\"(?:[^\\\"]*\\\"[^\\\"]*\\\")?(;self\\.AMPLITUDE_KEY)" );
    DateTime _lastClientTime = default;
    string _lastClientId;
    async Task<string?> GetImgurClientId()
    {
        if( _lastClientId != null && _lastClientTime - TimeSpan.FromMinutes( 5 ) < DateTime.UtcNow )
        {
            return _lastClientId;
        }
        var response = await _httpClient.GetAsync( "https://imgur.com/" );
        if( !response.IsSuccessStatusCode ) return null;

        var html = await response.Content.ReadAsStringAsync();
        var match = _htmlRegex.Match( html );
        if( !match.Success ) return null;

        var mainJsPath = match.Value;
        var mainJsResponse = await _httpClient.GetAsync( mainJsPath );
        if( !mainJsResponse.IsSuccessStatusCode ) return null;

        var jsCode = await mainJsResponse.Content.ReadAsStringAsync();
        // https://api.imgur.com/3/configuration/desktop?client_id=546c25a59c58ad7
        var jsMatch = _clientRegex.Match( jsCode );
        _lastClientId = jsMatch.Groups[1].Captures[0].Value;
        _lastClientTime = DateTime.UtcNow;
        return _lastClientId;
    }

    class ResponseJson
    {
        public class MediaJson
        {
            public string Url { get; set; }
        }
        public MediaJson[] Media { get; set; }
    }

    async Task<IEnumerable<string>> GetAlbumLinks( string albumUrl )
    {
        var albumId = new Uri( albumUrl ).Segments[2];
        var clientId = await GetImgurClientId();
        var response = await _httpClient.GetAsync( $"https://api.imgur.com/post/v1/albums/{albumId}?client_id={clientId}&include=media" );
        var parsed = await response.Content.ReadFromJsonAsync<ResponseJson>();
        return parsed.Media.Select( s => s.Url );
    }

    static bool IsAlbumLink( string url )
    {
        if( !Uri.TryCreate( url, UriKind.Absolute, out var uri ) ) return false;
        if( uri.Segments.Length < 3 ) return false;
        return uri.Segments[1] == "a";
    }

    [SlashCommand( "imgur_backup", "Backup all imgur images to discord." )]
    public async Task FindImgurLinks()
    {
        await DeferAsync();
        // search for messages containing images in the current channel
        var messages = Context.Channel.GetMessagesAsync( int.MaxValue, CacheMode.AllowDownload, new RequestOptions()
        {
            RetryMode = RetryMode.RetryRatelimit
        } ).Flatten();

        int i = 0;
        // count the images per hostname
        var links = messages
            .Select( s =>
            {
                Interlocked.Increment( ref i ); // Messages counting hack.
                return s;
            } )
            .SelectMany( s => s.Content.Split( "\n" ).Concat( s.Embeds.Select( s => s.Url ) ).ToAsyncEnumerable() )
            .Distinct()
            .Where( s => Uri.TryCreate( s, UriKind.Absolute, out var uri ) && (uri.Scheme == "http" || uri.Scheme == "https") )
            .Where( s =>
            {
                var host = new Uri( s ).Host;
                return host == "i.imgur.com" || host == "imgur.com";
            } )
            .Distinct();
        int albumCount = 0;
        int albumImageCount = 0;
        int imageCount = 0;
        await foreach( var link in links )
        {
            if( IsAlbumLink( link ) )
            {
                albumCount += 1;
                albumImageCount += (await GetAlbumLinks( link )).Count();
            }
            else
            {
                imageCount += 1;
            }
        }
        var str = $"Processed {i} messages.\n{albumCount} albums which contain a sum of {albumImageCount} images.\n{imageCount} images link.";
        try
        {
            await FollowupAsync( str );
        }
        catch( InvalidOperationException )
        {
            await Context.Channel.SendMessageAsync( str );
        }
    }


    [SlashCommand( "imagehosts", "List all image hostnames in the current channel." )]
    public async Task ListImageHosts()
    {
        await DeferAsync();
        // search for messages containing images in the current channel
        var messages = Context.Channel.GetMessagesAsync( int.MaxValue, CacheMode.AllowDownload, new RequestOptions()
        {
            RetryMode = RetryMode.RetryRatelimit
        } ).Flatten();

        int i = 0;
        IMessage first = null;
        IMessage last = null;
        // count the images per hostname
        var hostCounts = await messages
            .Select( s =>
            {
                Interlocked.Increment( ref i ); // Messages counting hack.
                if( first is null || s.Timestamp < first.Timestamp )
                {
                    first = s;
                }
                if( last is null || s.Timestamp > last.Timestamp )
                {
                    last = s;
                }
                return s;
            } )
            .SelectMany( s => s.Content.Split( "\n" ).Concat( s.Embeds.Select( s => s.Url ) ).ToAsyncEnumerable() )
            .Distinct()
            .Where( s => Uri.TryCreate( s, UriKind.Absolute, out var uri ) && (uri.Scheme == "http" || uri.Scheme == "https") )
            .GroupBy( s => new Uri( s ).Host )
            .ToDictionaryAwaitAsync( g => new ValueTask<string>( g.Key ), async g => await g.CountAsync() );
        var text = $"Earliest message scanned: {first.GetJumpUrl()}\n Latest message scanned: {last.GetJumpUrl()}\n {i} messages scanned.\n" + string.Join( "\n", hostCounts.Select( s => $"{s.Key}: {s.Value} image{(s.Value > 1 ? "s" : "")}." ) );
        Console.WriteLine( text );
        await FollowupAsync( text );
    }
}
