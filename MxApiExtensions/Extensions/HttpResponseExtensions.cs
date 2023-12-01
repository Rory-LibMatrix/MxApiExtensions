namespace MxApiExtensions.Extensions;

public static class HttpResponseExtensions {
    public static async Task WriteHttpResponse(this HttpResponse response, HttpResponseMessage message) {
        response.StatusCode = (int)message.StatusCode;
        //copy all headers
        foreach (var header in message.Headers) {
            response.Headers.Append(header.Key, header.Value.ToArray());
        }

        await response.StartAsync();
        var content = await message.Content.ReadAsStreamAsync();
        await content.CopyToAsync(response.Body);
        await response.CompleteAsync();
        // await content.DisposeAsync();
    }
}
