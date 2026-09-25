using System.Net;
using System.Text;
using System.Text.Json;
using FanHub.NotificationService.Push;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Http;
using Xunit;

namespace FanHub.NotificationService.IntegrationTests;

// Exercise the real Firebase SDK serializer and error parser, without contacting Google or a device.
public sealed class FirebaseSenderTests
{
    private sealed class Transport(HttpStatusCode status, string response) : HttpMessageHandler
    {
        public string? Body { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal("fcm.googleapis.com", request.RequestUri!.Host);
            Body = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(status) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }
    }
    private sealed class Factory(Transport transport) : Google.Apis.Http.HttpClientFactory
    {
        protected override HttpMessageHandler CreateHandler(CreateHttpClientArgs args) => transport;
    }
    [Fact]
    public async Task Sdk_sends_only_generic_content_and_stable_notification_id()
    {
        var transport = new Transport(HttpStatusCode.OK, "{\"name\":\"projects/test/messages/123\"}");
        var app = FirebaseApp.Create(new AppOptions { ProjectId = "test", Credential = GoogleCredential.FromAccessToken("test-only"), HttpClientFactory = new Factory(transport) }, Guid.NewGuid().ToString());
        try
        {
            var id = Guid.NewGuid();
            var result = await new FirebasePushSender(FirebaseMessaging.GetMessaging(app)).SendAsync("test-registration-token", id, default);
            Assert.Equal("Sent", result.Outcome);
            var body = JsonDocument.Parse(transport.Body!).RootElement.GetProperty("message");
            Assert.Equal(id.ToString(), body.GetProperty("data").GetProperty("notification_id").GetString());
            Assert.Equal("FanHub", body.GetProperty("notification").GetProperty("title").GetString());
            Assert.Equal(id.ToString(), body.GetProperty("android").GetProperty("notification").GetProperty("tag").GetString());
            Assert.Single(body.GetProperty("data").EnumerateObject());
        }
        finally { app.Delete(); }
    }
    [Theory]
    [InlineData("UNREGISTERED", 404, "NOT_FOUND", "InvalidToken")]
    [InlineData("INVALID_ARGUMENT", 400, "INVALID_ARGUMENT", "Permanent")]
    [InlineData("SENDER_ID_MISMATCH", 403, "PERMISSION_DENIED", "Permanent")]
    public async Task Sdk_errors_are_classified_without_exposing_tokens(string code, int http, string status, string outcome)
    {
        var response = JsonSerializer.Serialize(new { error = new { code = http, message = "provider diagnostic", status,
            details = new[] { new Dictionary<string, string> { ["@type"] = "type.googleapis.com/google.firebase.fcm.v1.FcmError", ["errorCode"] = code } } } });
        var app = FirebaseApp.Create(new AppOptions { ProjectId = "test", Credential = GoogleCredential.FromAccessToken("test-only"),
            HttpClientFactory = new Factory(new Transport((HttpStatusCode)http, response)) }, Guid.NewGuid().ToString());
        try
        {
            var result = await new FirebasePushSender(FirebaseMessaging.GetMessaging(app)).SendAsync("test-registration-token", Guid.NewGuid(), default);
            Assert.Equal(outcome, result.Outcome); Assert.DoesNotContain("diagnostic", result.Error!);
        }
        finally { app.Delete(); }
    }
}
