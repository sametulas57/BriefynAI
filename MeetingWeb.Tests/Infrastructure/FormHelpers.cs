using System.Text.RegularExpressions;

namespace MeetingWeb.Tests.Infrastructure
{
    public static class FormHelpers
    {
        private static readonly Regex TokenInput = new(
            "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"", RegexOptions.Compiled);

        // Antiforgery tokens are bound to the user and cookie, not to a page,
        // so a token from a page the user CAN open is valid for a POST against any page.
        public static async Task<string> GetAntiforgeryTokenAsync(HttpClient client, string pageUrl)
        {
            var html = await client.GetStringAsync(pageUrl);
            var match = TokenInput.Match(html);
            if (!match.Success) throw new InvalidOperationException($"No antiforgery token on {pageUrl}");
            return match.Groups[1].Value;
        }

        public static Task<HttpResponseMessage> PostFormAsync(
            HttpClient client, string url, string token, params (string Key, string Value)[] fields)
        {
            var pairs = fields.Select(f => new KeyValuePair<string, string>(f.Key, f.Value)).ToList();
            pairs.Add(new("__RequestVerificationToken", token));
            return client.PostAsync(url, new FormUrlEncodedContent(pairs));
        }

        // The same form sent by the page's script, which asks for a short answer instead of a reload.
        public static async Task<System.Text.Json.JsonElement> PostFormForJsonAsync(
            HttpClient client, string url, string token, params (string Key, string Value)[] fields)
        {
            var pairs = fields.Select(f => new KeyValuePair<string, string>(f.Key, f.Value)).ToList();
            pairs.Add(new("__RequestVerificationToken", token));
            using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(pairs) };
            request.Headers.Accept.ParseAdd("application/json");
            var res = await client.SendAsync(request);
            res.EnsureSuccessStatusCode();
            return System.Text.Json.JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        }
    }
}
