﻿//-----------------------------------------------------------------------
// <copyright file="TestSchedulerTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.TestKit;
using Akka.TestKit.Configs;
using FluentAssertions;
using Xunit;

namespace Akka.Hosting.TestKit.Tests;

public class TestSchedulerTests : TestKit
{
    private IActorRef? _testReceiveActor;

    protected override void ConfigureAkka(AkkaConfigurationBuilder builder, IServiceProvider provider)
    {
       
    }

    protected override async Task BeforeTestStart()
    {
        await base.BeforeTestStart();
        _testReceiveActor = Sys.ActorOf(Props.Create(() => new TestReceiveActor())
            .WithDispatcher(CallingThreadDispatcher.Id));
    }

    protected override Config Config { get; } = TestConfigs.TestSchedulerConfig; 

    [Fact]
    public void Delivers_message_when_scheduled_time_reached()
    {
        _testReceiveActor.Tell(new ScheduleOnceMessage(TimeSpan.FromSeconds(1)));
        _testReceiveActor.Ask<ActorIdentity>(new Identify(null), RemainingOrDefault)
            .Wait(RemainingOrDefault).Should().BeTrue(); // verify that the ActorCell has started

        Scheduler.Advance(TimeSpan.FromSeconds(1));
        ExpectMsg<ScheduleOnceMessage>();
    }

    [Fact]
    public void Does_not_deliver_message_prematurely()
    {
        _testReceiveActor.Tell(new ScheduleOnceMessage(TimeSpan.FromSeconds(1)));
        _testReceiveActor.Ask<ActorIdentity>(new Identify(null), RemainingOrDefault)
            .Wait(RemainingOrDefault).Should().BeTrue(); // verify that the ActorCell has started

        Scheduler.Advance(TimeSpan.FromMilliseconds(999));
        ExpectNoMsg(TimeSpan.FromMilliseconds(20));
    }

    [Fact]
    public void Delivers_messages_scheduled_for_same_time_in_order_they_were_added()
    {
        _testReceiveActor.Tell(new ScheduleOnceMessage(TimeSpan.FromSeconds(1), 1));
        _testReceiveActor.Tell(new ScheduleOnceMessage(TimeSpan.FromSeconds(1), 2));
        _testReceiveActor.Ask<ActorIdentity>(new Identify(null), RemainingOrDefault)
            .Wait(RemainingOrDefault).Should().BeTrue(); // verify that the ActorCell has started

        Scheduler.Advance(TimeSpan.FromSeconds(1));
        var firstId = ExpectMsg<ScheduleOnceMessage>().Id;
        var secondId = ExpectMsg<ScheduleOnceMessage>().Id;
        Assert.Equal(1, firstId);
        Assert.Equal(2, secondId);
    }

    [Fact]
    public void Keeps_delivering_rescheduled_message()
    {
        _testReceiveActor.Tell(new RescheduleMessage(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5)));
        _testReceiveActor.Ask<ActorIdentity>(new Identify(null), RemainingOrDefault)
            .Wait(RemainingOrDefault).Should().BeTrue(); // verify that the ActorCell has started

        for (int i = 0; i < 500; i ++)
        {
            Scheduler.Advance(TimeSpan.FromSeconds(5));
            ExpectMsg<RescheduleMessage>();
        }
    }

    [Fact]
    public void Uses_initial_delay_to_schedule_first_rescheduled_message()
    {
        _testReceiveActor.Tell(new RescheduleMessage(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5)));
        _testReceiveActor.Ask<ActorIdentity>(new Identify(null), RemainingOrDefault)
            .Wait(RemainingOrDefault).Should().BeTrue(); // verify that the ActorCell has started

        Scheduler.Advance(TimeSpan.FromSeconds(1));
        ExpectMsg<RescheduleMessage>();
    }

    [Fact]
    public void Doesnt_reschedule_cancelled()
    {
        _testReceiveActor.Tell(new CancelableMessage(TimeSpan.FromSeconds(1)));
        _testReceiveActor.Ask<ActorIdentity>(new Identify(null), RemainingOrDefault)
            .Wait(RemainingOrDefault).Should().BeTrue(); // verify that the ActorCell has started

        Scheduler.Advance(TimeSpan.FromSeconds(1));
        ExpectMsg<CancelableMessage>();
        _testReceiveActor.Tell(new CancelMessage());
        Scheduler.Advance(TimeSpan.FromSeconds(1));
        ExpectNoMsg(TimeSpan.FromMilliseconds(20));
    }


    [Fact]
    public void Advance_to_takes_us_to_correct_time()
    {
        _testReceiveActor.Tell(new ScheduleOnceMessage(TimeSpan.FromSeconds(1), 1));
        _testReceiveActor.Tell(new ScheduleOnceMessage(TimeSpan.FromSeconds(2), 2));
        _testReceiveActor.Tell(new ScheduleOnceMessage(TimeSpan.FromSeconds(3), 3));
        _testReceiveActor.Ask<ActorIdentity>(new Identify(null), RemainingOrDefault)
            .Wait(RemainingOrDefault).Should().BeTrue(); // verify that the ActorCell has started

        Scheduler.AdvanceTo(Scheduler.Now.AddSeconds(2));
        var firstId = ExpectMsg<ScheduleOnceMessage>().Id;
        var secondId = ExpectMsg<ScheduleOnceMessage>().Id;
        ExpectNoMsg(TimeSpan.FromMilliseconds(20));
        Assert.Equal(1, firstId);
        Assert.Equal(2, secondId);
    }

    private class TestReceiveActor : ReceiveActor
    {
        private Cancelable? _cancelable;

        public TestReceiveActor()
        {
            Receive<ScheduleOnceMessage>(x =>
            {
                Context.System.Scheduler.ScheduleTellOnce(x.ScheduleOffset, Sender, x, Self);
            });

            Receive<RescheduleMessage>(x =>
            {
                Context.System.Scheduler.ScheduleTellRepeatedly(x.InitialOffset, x.ScheduleOffset, Sender, x, Self);
            });

            Receive<CancelableMessage>(x =>
            {
                _cancelable = new Cancelable(Context.System.Scheduler);
                Context.System.Scheduler.ScheduleTellRepeatedly(x.ScheduleOffset, x.ScheduleOffset, Sender, x, Self, _cancelable);
            });

            Receive<CancelMessage>(_ =>
            {
                if (_cancelable is null)
                    throw new NullReferenceException("_cancelable is null, actor has not received any CancelableMessage message");
                
                _cancelable.Cancel();
            });

        }
    }

    private class CancelableMessage
    {
        public TimeSpan ScheduleOffset { get; }
        public int Id { get; }

        public CancelableMessage(TimeSpan scheduleOffset, int id = 1)
        {
            ScheduleOffset = scheduleOffset;
            Id = id;
        }
    }

    private class CancelMessage { }

    private class ScheduleOnceMessage
    {
        public TimeSpan ScheduleOffset { get; }
        public int Id { get; }

        public ScheduleOnceMessage(TimeSpan scheduleOffset, int id = 1)
        {
            ScheduleOffset = scheduleOffset;
            Id = id;
        }
    }

    private class RescheduleMessage
    {
        public TimeSpan InitialOffset { get; }
        public TimeSpan ScheduleOffset { get; }
        public int Id { get; }

        public RescheduleMessage(TimeSpan initialOffset, TimeSpan scheduleOffset, int id = 1)
        {
            InitialOffset = initialOffset;
            ScheduleOffset = scheduleOffset;
            Id = id;
        }
    }


    private TestScheduler Scheduler => (TestScheduler)Sys.Scheduler;
}