﻿//-----------------------------------------------------------------------
// <copyright file="DilatedTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Diagnostics;
using Akka.Configuration;
using Xunit;
using Xunit.Sdk;
using FluentAssertions;
using static FluentAssertions.FluentActions;

namespace Akka.Hosting.TestKit.Tests.TestKitBaseTests;

public class DilatedTests : TestKit
{
    private const int TimeFactor = 4;
    private const int Timeout = 1000;
    private const int ExpectedTimeout = Timeout * TimeFactor;
    private const int Margin = 1000; // margin for GC
    private const int DiffDelta = 100; 

    protected override Config Config { get; } = $"akka.test.timefactor={TimeFactor}";

    protected override void ConfigureAkka(AkkaConfigurationBuilder builder, IServiceProvider provider)
    {
    }

    [Fact]
    public void Dilates_correctly_using_timeFactor()
    {
        Assert.Equal(Dilated(TimeSpan.FromMilliseconds(Timeout)), TimeSpan.FromMilliseconds(ExpectedTimeout));
    }

    [Fact]
    public void AwaitCondition_should_dilate_timeout()
    {
        var stopwatch = Stopwatch.StartNew();
        Invoking(() => AwaitCondition(() => false, TimeSpan.FromMilliseconds(Timeout)))
            .Should().Throw<TrueException>();
        stopwatch.Stop();
        AssertDilated(stopwatch.ElapsedMilliseconds, $"Expected the timeout to be {ExpectedTimeout} but in fact it was {stopwatch.ElapsedMilliseconds}.");
    }

    [Fact]
    public void ReceiveN_should_dilate_timeout()
    {
        var stopwatch = Stopwatch.StartNew();
        Invoking(() => ReceiveN(42, TimeSpan.FromMilliseconds(Timeout)))
            .Should().Throw<TrueException>();
        stopwatch.Stop();
        AssertDilated(stopwatch.ElapsedMilliseconds, $"Expected the timeout to be {ExpectedTimeout} but in fact it was {stopwatch.ElapsedMilliseconds}.");
    }

    [Fact]
    public void ExpectMsgAllOf_should_dilate_timeout()
    {
        var stopwatch = Stopwatch.StartNew();
        Invoking(() => ExpectMsgAllOf(TimeSpan.FromMilliseconds(Timeout), "1", "2"))
            .Should().Throw<TrueException>();
        stopwatch.Stop();
        AssertDilated(stopwatch.ElapsedMilliseconds, $"Expected the timeout to be {ExpectedTimeout} but in fact it was {stopwatch.ElapsedMilliseconds}.");
    }

    [Fact]
    public void FishForMessage_should_dilate_timeout()
    {
        var stopwatch = Stopwatch.StartNew();
        Invoking(() => FishForMessage(_=>false, TimeSpan.FromMilliseconds(Timeout)))
            .Should().Throw<TrueException>();
        stopwatch.Stop();
        AssertDilated(stopwatch.ElapsedMilliseconds, $"Expected the timeout to be {ExpectedTimeout} but in fact it was {stopwatch.ElapsedMilliseconds}.");
    }

    private static void AssertDilated(double diff, string? message = null)
    {
        Assert.True(diff >= ExpectedTimeout - DiffDelta, message);
        Assert.True(diff < ExpectedTimeout + Margin, message); // margin for GC
    }
}