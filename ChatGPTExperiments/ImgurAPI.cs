using Discord;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace KuinoxSemiAGI
{
    static class ImgurAPI
    {
        static readonly Regex _htmlRegex = new( "https:\\/\\/s\\.imgur\\.com\\/desktop-assets\\/js\\/main\\.[a-f\\d]+\\.js" );
        static readonly Regex _clientRegex = new( "\"([^\\\"]*)\\\"(?:[^\\\"]*\\\"[^\\\"]*\\\")?(;self\\.AMPLITUDE_KEY)" );
        static readonly Regex _galleryRegexData = new( "(?:<script>window.postDataJSON=)(.+?)(?:<\\/script>)" );
        static DateTime _lastClientTime = default;
        static string? _lastClientId;

        public static async Task<string?> GetImgurClientId( HttpClient httpClient )
        {
            if( _lastClientId != null && _lastClientTime - TimeSpan.FromMinutes( 5 ) < DateTime.UtcNow )
            {
                return _lastClientId;
            }
            var response = await httpClient.GetAsync( "https://imgur.com/" );
            if( !response.IsSuccessStatusCode ) return null;

            var html = await response.Content.ReadAsStringAsync();
            var match = _htmlRegex.Match( html );
            if( !match.Success ) return null;

            var mainJsPath = match.Value;
            var mainJsResponse = await httpClient.GetAsync( mainJsPath );
            if( !mainJsResponse.IsSuccessStatusCode ) return null;

            var jsCode = await mainJsResponse.Content.ReadAsStringAsync();
            // https://api.imgur.com/3/configuration/desktop?client_id=546c25a59c58ad7
            var jsMatch = _clientRegex.Match( jsCode );
            _lastClientId = jsMatch.Groups[1].Captures[0].Value;
            _lastClientTime = DateTime.UtcNow;
            return _lastClientId;
        }

        public static async Task<ImgurResponseJson?> GetAlbumData( HttpClient httpClient, string albumUrl )
        {
            var albumId = GetImgurId( albumUrl );
            var clientId = await GetImgurClientId( httpClient );
            var response = await httpClient.GetAsync( $"https://api.imgur.com/post/v1/albums/{albumId}?client_id={clientId}&include=media" );
            if( !response.IsSuccessStatusCode ) return null;
            var parsed = await response.Content.ReadFromJsonAsync<ImgurResponseJson>();
            return parsed!;
        }
        public static async Task<ImgurResponseJson?> GetPostData( HttpClient httpClient, string imgurLink )
        {
            var albumId = GetImgurId( imgurLink );
            var clientId = await GetImgurClientId(httpClient);
            var response = await httpClient.GetAsync( $"https://api.imgur.com/post/v1/media/{albumId}?client_id={clientId}&include=media" );
            if( !response.IsSuccessStatusCode ) return null;
            var parsed = await response.Content.ReadFromJsonAsync<ImgurResponseJson>();
            return parsed!;
        }

        public static async Task<ImgurResponseJson?> GetGalleryData( HttpClient httpClient, string imgurLink )
        {
            var res = await httpClient.GetAsync( imgurLink );
            if( !res.IsSuccessStatusCode ) return null;
            var html = await res.Content.ReadAsStringAsync();
            var match = _galleryRegexData.Match( html );
            var str = JsonSerializer.Deserialize<string>( match.Groups[1].Value );
            return JsonSerializer.Deserialize<ImgurResponseJson>( str! );
        }

        public static bool IsAlbumLink( string url )
        {
            if( !Uri.TryCreate( url, UriKind.Absolute, out var uri ) ) return false;
            if( uri.Segments.Length < 3 ) return false;
            return uri.Segments[1] == "a/";
        }

        public static bool IsGalleryLink( string url )
        {
            if( !Uri.TryCreate( url, UriKind.Absolute, out var uri ) ) return false;
            if( uri.Segments.Length < 3 ) return false;
            return uri.Segments[1] == "gallery/";
        }

        public static string GetImgurId( string url )
            => Path.GetFileNameWithoutExtension( new Uri( url ).Segments.Last() );

        public static IEnumerable<string> ExtractMsgLinks( IMessage msg )
            => msg.Content.Split( new char[] { '\n', ' ' } )
                .Where( s => Uri.TryCreate( s, UriKind.Absolute, out _ ) )
                .Concat( msg.Embeds.Select( s => s.Url ) );

        public class ImgurResponseJson
        {
            public class MediaJson
            {
                public string? url { get; set; }
            }
            public string? title { get; set; }
            public MediaJson[]? media { get; set; }
        }
    }
}
