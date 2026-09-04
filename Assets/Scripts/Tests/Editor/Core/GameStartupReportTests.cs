#nullable enable

using System;
using Fodinae.Core;
using NUnit.Framework;

namespace Fodinae.Tests.Core;

public sealed class GameStartupReportTests
{
    [Test]
    public void ThrowIfCritical_DegradedIssueDoesNotFailStartup()
    {
        var report = new GameStartupReport();
        report.Degraded("audio", "no audio device");

        Assert.DoesNotThrow(report.ThrowIfCritical);
        Assert.That(report.Issues, Has.Count.EqualTo(1));
        Assert.That(report.Issues[0].Severity, Is.EqualTo(StartupIssueSeverity.Degraded));
    }

    [Test]
    public void ThrowIfCritical_CriticalIssueContainsSystemAndMessage()
    {
        var report = new GameStartupReport();
        report.Critical("lighting", "compute shader missing");

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            report.ThrowIfCritical)!;

        Assert.That(exception.Message, Does.Contain("lighting"));
        Assert.That(exception.Message, Does.Contain("compute shader missing"));
    }

    [Test]
    public void RunCritical_CapturesFailureAndContinuesCollectingIssues()
    {
        var report = new GameStartupReport();
        var original = new InvalidOperationException("subscription failed");
        bool secondSystemRan = false;

        report.RunCritical(
            "network",
            () => throw original);
        report.RunCritical("assets", () => secondSystemRan = true);
        report.Critical("lighting", "shader missing");

        Assert.That(secondSystemRan, Is.True);
        Assert.That(report.Issues, Has.Count.EqualTo(2));
        Assert.That(report.Issues[0].Exception, Is.SameAs(original));
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            report.ThrowIfCritical)!;
        Assert.That(exception.Message, Does.Contain("network: subscription failed"));
        Assert.That(exception.Message, Does.Contain("lighting: shader missing"));
        Assert.That(exception.InnerException, Is.TypeOf<AggregateException>());
        var aggregate = (AggregateException)exception.InnerException!;
        Assert.That(aggregate.InnerExceptions, Does.Contain(original));
    }
}
