using System.Text.Json;
using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Integrations.Captcha;

namespace HarvestAdapters.Tests.Unit.Integrations;

public sealed class TwoCaptchaSolverTests
{
    [Fact]
    public async Task Submit_ShouldSendNothing_WhenIntegrationsAreDisabled()
    {
        var host = new IntegrationTestHost();
        (await host.Captcha.SubmitAsync(IntegrationTestHost.Task())).Status.Should().Be("disabled");
        host.Http.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Submit_ShouldReportUnavailableWithoutNetwork_WhenCredentialsAreMissing()
    {
        var host = new IntegrationTestHost(); host.EnableCaptcha(); host.Options.Captcha.ApiKey = null;
        (await host.Captcha.SubmitAsync(IntegrationTestHost.Task())).Status.Should().Be("unavailable");
        host.Http.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Submit_ShouldUseTheV2ContractWithoutCookiesOrQuerySecrets_WhenExplicitlyAllowed()
    {
        var host = new IntegrationTestHost(); host.EnableCaptcha(); host.Http.Replies.Enqueue("""{"errorId":0,"taskId":1234567890123}""");
        CaptchaTask task = IntegrationTestHost.Task() with { WebsiteUrl = new Uri("https://source.example.test/event?session=private#fragment") };
        CaptchaTaskResult result = await host.Captcha.SubmitAsync(task);
        result.TaskId.Should().Be(1234567890123);
        result.Status.Should().Be("submitted");
        ObservedRequest request = host.Http.Calls.Single();
        request.Uri.AbsoluteUri.Should().Be("https://api.2captcha.com/createTask");
        request.Headers.Should().NotContainKey("Cookie").And.NotContainKey("Authorization");
        using JsonDocument body = JsonDocument.Parse(request.Body!);
        body.RootElement.GetProperty("clientKey").GetString().Should().Be("provider-test-key");
        body.RootElement.GetProperty("task").GetProperty("type").GetString().Should().Be("RecaptchaV2TaskProxyless");
        body.RootElement.GetProperty("task").GetProperty("websiteURL").GetString().Should().Be("https://source.example.test/event");
        request.Body.Should().NotContain("private");
    }

    [Theory]
    [InlineData("https://foreign.example.test/event")]
    [InlineData("http://source.example.test/event")]
    [InlineData("https://source.example.test:444/event")]
    public async Task Submit_ShouldRejectUnapprovedOrigins_WithoutCallingTheProvider(string url)
    {
        var host = new IntegrationTestHost(); host.EnableCaptcha();
        CaptchaTaskResult result = await host.Captcha.SubmitAsync(IntegrationTestHost.Task() with { WebsiteUrl = new Uri(url) });
        result.ErrorCode.Should().Be("SOURCE_NOT_ALLOWED");
        host.Http.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task GetResult_ShouldReadTurnstileTokenAndCost_WithoutEchoingTheTokenInDiagnostics()
    {
        var host = new IntegrationTestHost(); host.EnableCaptcha();
        host.Http.Replies.Enqueue("""{"errorId":0,"status":"ready","solution":{"token":"private-token","userAgent":"worker-agent"},"cost":"0.0012"}""");
        CaptchaTaskResult result = await host.Captcha.GetResultAsync(42);
        result.Status.Should().Be("ready"); result.Token.Should().Be("private-token"); result.CostUsd.Should().Be(0.0012m);
        result.ToString().Should().NotContain("private-token");
    }

    [Fact]
    public async Task Submit_ShouldSanitizeProviderDiagnostics_WhenTheProviderReturnsAnError()
    {
        var host = new IntegrationTestHost(); host.EnableCaptcha();
        host.Http.Replies.Enqueue("""{"errorId":1,"errorCode":"bad key provider-test-key","errorDescription":"provider-test-key secret"}""");
        CaptchaTaskResult result = await host.Captcha.SubmitAsync(IntegrationTestHost.Task());
        result.ErrorCode.Should().Be("PROVIDER_ERROR"); result.ToString().Should().NotContain("provider-test-key");
    }

    [Fact]
    public async Task Submit_ShouldRejectAnOversizedResponse_WithoutExposingItsBody()
    {
        var host = new IntegrationTestHost(); host.EnableCaptcha();
        host.Http.Replies.Enqueue(new string('x', 65_537));
        CaptchaTaskResult result = await host.Captcha.SubmitAsync(IntegrationTestHost.Task());
        result.ErrorCode.Should().Be("PROVIDER_UNAVAILABLE"); host.Http.Calls.Should().HaveCount(1);
    }

    [Fact]
    public async Task Submit_ShouldRedactTheApiKey_EvenInsideAnOtherwiseValidErrorCode()
    {
        var host = new IntegrationTestHost(); host.EnableCaptcha(); host.Options.Captcha.ApiKey = "testsecret";
        host.Http.Replies.Enqueue("""{"errorId":1,"errorCode":"ERROR_testsecret"}""");
        CaptchaTaskResult result = await host.Captcha.SubmitAsync(IntegrationTestHost.Task());
        result.ErrorCode.Should().Be("PROVIDER_ERROR"); result.ToString().Should().NotContain("testsecret");
    }

    [Fact]
    public async Task Solve_ShouldSubmitOnceAndStopAtItsPollLimit_WhenTheTaskKeepsProcessing()
    {
        var host = new IntegrationTestHost(); host.EnableCaptcha(); host.Options.Captcha.MaximumPolls = 2;
        host.Http.Replies.Enqueue("""{"errorId":0,"taskId":42}""");
        host.Http.Replies.Enqueue("""{"errorId":0,"status":"processing"}""");
        host.Http.Replies.Enqueue("""{"errorId":0,"status":"processing"}""");
        Task<CaptchaTaskResult> solving = host.Captcha.SolveAsync(IntegrationTestHost.Task());
        await UntilAsync(() => host.Http.Calls.Count == 1);
        host.Clock.Advance(TimeSpan.FromSeconds(5));
        await UntilAsync(() => host.Http.Calls.Count == 2);
        host.Clock.Advance(TimeSpan.FromSeconds(5));
        CaptchaTaskResult result = await solving;
        result.ErrorCode.Should().Be("POLL_LIMIT");
        host.Http.Calls.Count(call => call.Uri.AbsolutePath == "/createTask").Should().Be(1);
        host.Http.Calls.Count(call => call.Uri.AbsolutePath == "/getTaskResult").Should().Be(2);
    }

    [Fact]
    public async Task Solve_ShouldStopAtTheDeadline_WithoutResubmitting()
    {
        var host = new IntegrationTestHost(); host.EnableCaptcha(); host.Options.Captcha.SolveTimeout = TimeSpan.FromSeconds(11);
        host.Http.Replies.Enqueue("""{"errorId":0,"taskId":42}""");
        host.Http.BeforeReply = async ct =>
        {
            if (host.Http.Calls.Count > 1)
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        };
        Task<CaptchaTaskResult> solving = host.Captcha.SolveAsync(IntegrationTestHost.Task());
        await UntilAsync(() => host.Http.Calls.Count == 1);
        host.Clock.Advance(TimeSpan.FromSeconds(5));
        await UntilAsync(() => host.Http.Calls.Count == 2);
        host.Clock.Advance(TimeSpan.FromSeconds(6));
        CaptchaTaskResult result = await solving;
        result.Status.Should().Be("timeout"); host.Http.Calls.Should().HaveCount(2);
        host.Http.Calls.Count(call => call.Uri.AbsolutePath == "/createTask").Should().Be(1);
    }

    [Fact]
    public async Task Submit_ShouldPropagateCancellation_WithoutRetry()
    {
        var host = new IntegrationTestHost(); host.EnableCaptcha();
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => host.Captcha.SubmitAsync(IntegrationTestHost.Task(), cancelled.Token));
        host.Http.Calls.Should().BeEmpty();
    }

    private static async Task UntilAsync(Func<bool> condition)
    {
        for (int index = 0; index < 1000 && !condition(); index++) await Task.Yield();
        condition().Should().BeTrue();
    }
}
