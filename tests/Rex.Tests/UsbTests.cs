using System.Text.Json.Nodes;
using System.Xml.Linq;
using Rex.Cli;
using Rex.Core;
using Rex.Mirror;
using Rex.Mirror.Views;
using Rex.Tests.Support;

namespace Rex.Tests;

/// <summary>
/// USB recovery: which devices count as a phone Windows could not read, when the app repairs them
/// and what it says, the scheduled task that repairs without a prompt, the prompted repair, and
/// the CLI verbs. Nothing here touches the real PC: every test hands in its own devices, Task
/// Scheduler and pnputil.
/// </summary>
public sealed class UsbTests
{
    private static readonly DateTime T0 = new(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);

    private static UsbDeviceNode Failed(string instance = @"USB\VID_0000&PID_0002\5&1F007347&0&5", int code = 43) =>
        new(instance, "Unknown USB Device (Device Descriptor Request Failed)", [@"USB\DEVICE_DESCRIPTOR_FAILURE"], code);

    private static UsbDeviceNode Samsung(int code = 10) =>
        new(@"USB\VID_04E8&PID_6860\R3CRB04F7PP", "SAMSUNG Mobile USB Composite Device", [@"USB\VID_04E8&PID_6860&REV_0400"], code);

    // ----- Classifying -----

    [Fact]
    public void FailedEnumerations_AreRecognisedByIdVendorOrName()
    {
        var byId = UsbProblems.Classify(Failed())!;
        Assert.Equal(UsbProblemKind.FailedEnumeration, byId.Kind);
        Assert.True(byId.AutoRepairable);

        // VID 0000 alone, with ids Windows would not give up.
        var byVendor = UsbProblems.Classify(new UsbDeviceNode(@"USB\VID_0000&PID_0001\6&2", string.Empty, [], 43))!;
        Assert.Equal(UsbProblemKind.FailedEnumeration, byVendor.Kind);
        Assert.False(byVendor.AutoRepairable);

        foreach (var phrase in UsbProblems.FailurePhrases)
        {
            var byName = UsbProblems.Classify(new UsbDeviceNode(@"USB\ROOT_HUB30\x", $"Unknown USB Device ({phrase})", [], 43));
            Assert.Equal(UsbProblemKind.FailedEnumeration, byName?.Kind);
        }

        Assert.All(UsbProblems.FailedEnumerationIds, id => Assert.True(UsbProblems.IsFailedEnumerationId(" " + id.ToLowerInvariant() + " ")));
    }

    [Fact]
    public void PhoneDevices_AreRecognisedOnlyForPhoneMakersAndRealProblems()
    {
        foreach (var code in UsbProblems.PhoneProblemCodes)
        {
            var problem = UsbProblems.Classify(Samsung(code))!;
            Assert.Equal(UsbProblemKind.PhoneDevice, problem.Kind);
            Assert.Equal("Samsung", problem.Vendor);
            Assert.False(problem.AutoRepairable);
        }

        // A code a phone does not show, a device someone disabled, one with no problem, a dock
        // from a maker left out on purpose, and anything off the USB enumerator are all ignored.
        Assert.Null(UsbProblems.Classify(Samsung(code: 1)));
        Assert.Null(UsbProblems.Classify(Samsung(UsbProblems.DisabledByUser)));
        Assert.Null(UsbProblems.Classify(Samsung(code: 0)));
        Assert.Null(UsbProblems.Classify(new UsbDeviceNode(@"USB\VID_17EF&PID_A395\1", "ThinkPad Dock", [], 43)));
        Assert.Null(UsbProblems.Classify(new UsbDeviceNode(@"HID\VID_04E8&PID_6860\1", "HID device", [], 43)));
        Assert.Null(UsbProblems.Classify(Failed(code: UsbProblems.DisabledByUser)));

        // The vendor may only be in the hardware ids.
        var fromIds = UsbProblems.Classify(new UsbDeviceNode(@"USB\SOMETHING\1", "Pixel", [@"USB\VID_18D1&PID_4EE7"], 28))!;
        Assert.Equal("Google", fromIds.Vendor);
        Assert.Equal(2, UsbProblems.Classify([Failed(), Samsung(), Samsung(0)]).Count);
    }

    [Theory]
    [InlineData(@"USB\VID_04E8&PID_6860&MI_01\6&1", "04E8")]
    [InlineData(@"usb\vid_18d1&pid_4ee7", "18D1")]
    [InlineData(@"USB\XVID_04E8", null)]
    [InlineData(@"USB\VID_12", null)]
    [InlineData(@"USB\VID_ZZZZ&PID_0001", null)]
    [InlineData(@"USB\ROOT_HUB30\4&14A3880E", null)]
    public void VendorId_ReadsOnlyARealVidField(string id, string? vendor) => Assert.Equal(vendor, UsbProblems.VendorId(id));

    [Fact]
    public void Describe_SaysWhatWindowsCallsItAndWhatIsWrong()
    {
        Assert.Equal("Unknown USB Device (Device Descriptor Request Failed): Windows stopped it because it reported problems (code 43)",
            UsbProblems.Classify(Failed())!.Describe());
        Assert.Equal("Samsung USB device: its driver is not installed (code 28)",
            UsbProblems.Classify(Samsung(28) with { Description = string.Empty })!.Describe());
        Assert.Contains("problem code 99", UsbProblems.ProblemText(99), StringComparison.Ordinal);
    }

    [Fact]
    public void TheTestFileSource_ReadsNodesAndSurvivesAHalfWrittenFile()
    {
        var nodes = FileUsbDeviceSource.Parse("""
            [{"instanceId":"USB\\VID_0000&PID_0002\\5&1","description":"Unknown","hardwareIds":["USB\\DEVICE_DESCRIPTOR_FAILURE",""],"problemCode":43},
             {"description":"no id, skipped"}]
            """);
        var node = Assert.Single(nodes);
        Assert.Equal([@"USB\DEVICE_DESCRIPTOR_FAILURE"], node.HardwareIds);
        Assert.Equal(43, node.ProblemCode);

        var path = Path.Combine(Path.GetTempPath(), "rex-usb-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            Assert.Empty(new FileUsbDeviceSource(path).ProblemNodes());
            File.WriteAllText(path, "[{\"instanceId\":");
            Assert.Empty(new FileUsbDeviceSource(path).ProblemNodes());
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ----- When to repair -----

    [Fact]
    public void Policy_WaitsBeforeSpeakingThenRepairsAtMostOncePerInterval()
    {
        var policy = new UsbRecoveryPolicy();
        var failed = UsbProblems.Classify([Failed()]);

        Assert.Equal(UsbRecoveryDecision.Quiet, policy.Observe(T0, phoneReady: false, [], automatic: true));
        Assert.Equal(UsbNotice.None, policy.Observe(T0, false, failed, true).Notice);
        Assert.True(policy.InEpisode);
        Assert.False(policy.Observe(T0 + TimeSpan.FromSeconds(3), false, failed, true).Trigger);

        var first = policy.Observe(T0 + UsbRecoveryPolicy.FailedEnumerationDelay, false, failed, true);
        Assert.Equal((UsbNotice.Fixing, true, 1), (first.Notice, first.Trigger, first.Attempts));

        var soon = T0 + UsbRecoveryPolicy.FailedEnumerationDelay + TimeSpan.FromSeconds(30);
        Assert.Equal((UsbNotice.Fixing, false, 1), Tuple(policy.Observe(soon, false, failed, true)));

        var start = T0 + UsbRecoveryPolicy.FailedEnumerationDelay;
        Assert.True(policy.Observe(start + UsbRecoveryPolicy.MinimumInterval, false, failed, true).Trigger);
        Assert.True(policy.Observe(start + UsbRecoveryPolicy.MinimumInterval * 2, false, failed, true).Trigger);

        // Three attempts in one episode; after that, advice once the last one has had its time.
        var spent = policy.Observe(start + UsbRecoveryPolicy.MinimumInterval * 3, false, failed, true);
        Assert.Equal((UsbNotice.Advice, false, UsbRecoveryPolicy.AttemptsPerEpisode), Tuple(spent));
    }

    [Fact]
    public void Policy_WaitsLongerForAPhonesOwnDeviceWhichMayStillBeInstalling()
    {
        var policy = new UsbRecoveryPolicy();
        var phone = UsbProblems.Classify([Samsung()]);
        policy.Observe(T0, false, phone, automatic: false);
        Assert.Equal(UsbNotice.None, policy.Observe(T0 + UsbRecoveryPolicy.FailedEnumerationDelay, false, phone, false).Notice);
        Assert.Equal(UsbNotice.Offer, policy.Observe(T0 + UsbRecoveryPolicy.PhoneDeviceDelay, false, phone, false).Notice);
    }

    [Fact]
    public void Policy_OffersTheFixWhenItCannotRepairByItself_AndAdvisesAfterAPromptedRepair()
    {
        var policy = new UsbRecoveryPolicy();
        var failed = UsbProblems.Classify([Failed()]);
        policy.Observe(T0, false, failed, automatic: false);
        var now = T0 + UsbRecoveryPolicy.FailedEnumerationDelay;
        Assert.Equal((UsbNotice.Offer, false, 0), Tuple(policy.Observe(now, false, failed, false)));

        policy.RecordAttempt(now);
        Assert.Equal(1, policy.Attempts);
        Assert.Equal(UsbNotice.Offer, policy.Observe(now + TimeSpan.FromSeconds(10), false, failed, false).Notice);
        Assert.Equal(UsbNotice.Advice, policy.Observe(now + UsbRecoveryPolicy.SettleTime, false, failed, false).Notice);

        // A prompted repair also spaces out what the task may do next.
        var automatic = policy.Observe(now + UsbRecoveryPolicy.SettleTime + TimeSpan.FromSeconds(1), false, failed, automatic: true);
        Assert.False(automatic.Trigger);
    }

    [Fact]
    public void Policy_EpisodesEndWithThePhoneOrAfterALongAbsence_AndDismissalLastsOneEpisode()
    {
        var policy = new UsbRecoveryPolicy();
        var failed = UsbProblems.Classify([Failed()]);
        policy.Observe(T0, false, failed, true);
        var now = T0 + UsbRecoveryPolicy.FailedEnumerationDelay;
        Assert.True(policy.Observe(now, false, failed, true).Trigger);

        // A repair makes the node vanish for a moment: still the same episode, still saying so.
        Assert.Equal((UsbNotice.Fixing, false, 1), Tuple(policy.Observe(now + TimeSpan.FromSeconds(3), false, [], true)));
        Assert.Equal(1, policy.Observe(now + TimeSpan.FromSeconds(6), false, failed, true).Attempts);

        policy.Dismiss();
        Assert.Equal(UsbNotice.None, policy.Observe(now + TimeSpan.FromSeconds(9), false, failed, true).Notice);

        // The phone arrives: the episode, its attempts and the dismissal are over.
        Assert.Equal(UsbRecoveryDecision.Quiet, policy.Observe(now + TimeSpan.FromSeconds(12), phoneReady: true, failed, true));
        Assert.False(policy.InEpisode);
        Assert.Equal(0, policy.Attempts);

        // A long absence ends one too, and the next failure starts afresh and speaks again.
        policy.Observe(now + TimeSpan.FromSeconds(20), false, failed, true);
        policy.Observe(now + TimeSpan.FromSeconds(21), false, [], true);
        Assert.True(policy.InEpisode);
        Assert.Equal(UsbRecoveryDecision.Quiet, policy.Observe(now + TimeSpan.FromSeconds(21) + UsbRecoveryPolicy.EpisodeGap, false, [], true));
        Assert.False(policy.InEpisode);
    }

    [Fact]
    public void NoticeText_NamesTheDeviceTheAttemptsAndTheDriverWhenOneIsMissing()
    {
        var failed = UsbProblems.Classify([Failed()]);
        var fixing = UsbRecoveryText.For(UsbNotice.Fixing, failed, 2);
        Assert.Equal(UsbRecoveryText.FixingTitle, fixing.Title);
        Assert.Contains("Device Descriptor Request Failed", fixing.Text, StringComparison.Ordinal);
        Assert.Contains("attempt 2 of 3", fixing.Text, StringComparison.Ordinal);
        Assert.Contains("administrator approval", UsbRecoveryText.For(UsbNotice.Offer, [], 0).Text, StringComparison.Ordinal);
        Assert.Contains("a USB device it could not read", UsbRecoveryText.For(UsbNotice.Offer, [], 0).Text, StringComparison.Ordinal);

        var advice = UsbRecoveryText.For(UsbNotice.Advice, UsbProblems.Classify([Samsung(28)]), 3);
        Assert.Equal(UsbRecoveryText.AdviceTitle, advice.Title);
        Assert.Contains("another USB port", advice.Text, StringComparison.Ordinal);
        Assert.Contains("install Samsung's USB driver", advice.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("driver", UsbRecoveryText.For(UsbNotice.Advice, failed, 3).Text, StringComparison.Ordinal);
        Assert.Equal((string.Empty, string.Empty), UsbRecoveryText.For(UsbNotice.None, failed, 0));
    }

    [Fact]
    public void TheWindowOffersOnlyTheFixesThatApply()
    {
        Assert.Equal((true, true), MainWindow.UsbAnswers(UsbNotice.Offer, repairsAutomatically: false));
        Assert.Equal((true, false), MainWindow.UsbAnswers(UsbNotice.Offer, repairsAutomatically: true));
        Assert.Equal((true, false), MainWindow.UsbAnswers(UsbNotice.Advice, false));
        Assert.Equal((false, false), MainWindow.UsbAnswers(UsbNotice.Fixing, true));
        Assert.Equal((false, false), MainWindow.UsbAnswers(UsbNotice.None, false));

        Assert.StartsWith("Off", SettingsPanel.UsbSettingText(false, new UsbAutoRepairStatus(UsbAutoRepairState.Installed)), StringComparison.Ordinal);
        Assert.StartsWith("On", SettingsPanel.UsbSettingText(true, new UsbAutoRepairStatus(UsbAutoRepairState.Installed)), StringComparison.Ordinal);
        Assert.Contains("another version", SettingsPanel.UsbSettingText(true, new UsbAutoRepairStatus(UsbAutoRepairState.Outdated)), StringComparison.Ordinal);
        Assert.Contains("administrator approval", SettingsPanel.UsbSettingText(true, new UsbAutoRepairStatus(UsbAutoRepairState.NotInstalled)), StringComparison.Ordinal);
        Assert.True(new RexConfig().App.AutoRepairUsb);
    }

    // ----- The no-prompt task -----

    [Fact]
    public void Task_RunsOnlySystem32PnputilWithFixedArgumentsAsSystem()
    {
        const string system = @"C:\Windows\System32";
        var xml = XDocument.Parse(UsbAutoRepairTask.Xml(system));
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        var root = xml.Root!;

        Assert.Equal(UsbAutoRepairTask.SystemSid, root.Element(ns + "Principals")!.Element(ns + "Principal")!.Element(ns + "UserId")!.Value);
        Assert.Empty(root.Element(ns + "Triggers")!.Elements());
        Assert.Equal(UsbAutoRepairTask.SecurityDescriptor, root.Element(ns + "RegistrationInfo")!.Element(ns + "SecurityDescriptor")!.Value);

        var actions = root.Element(ns + "Actions")!.Elements().ToArray();
        Assert.Equal(UsbAutoRepairTask.Arguments.Count, actions.Length);
        Assert.All(actions, action =>
        {
            Assert.Equal("Exec", action.Name.LocalName);
            Assert.Equal(system + @"\pnputil.exe", action.Element(ns + "Command")!.Value);
        });

        // Restart, scan, remove, scan: and only ever the ids a hub gives a device it gave up on.
        var arguments = UsbAutoRepairTask.Arguments;
        Assert.Equal(UsbProblems.FailedEnumerationIds.Count * 2 + 2, arguments.Count);
        Assert.All(arguments.Where(a => a != "/scan-devices"), a =>
            Assert.Contains(UsbProblems.FailedEnumerationIds, id => a.EndsWith($"/deviceid \"{id}\"", StringComparison.Ordinal)));
        Assert.StartsWith("/restart-device", arguments[0], StringComparison.Ordinal);
        Assert.Equal("/scan-devices", arguments[^1]);

        // Only SYSTEM and Administrators may change it; people signed in may read and start it.
        Assert.Equal("O:BAG:SYD:P(A;;FA;;;SY)(A;;FA;;;BA)(A;;FRFX;;;IU)", UsbAutoRepairTask.SecurityDescriptor);
        Assert.True(UsbAutoRepairTask.IsCurrent(UsbAutoRepairTask.Xml(system), system));
    }

    [Fact]
    public void Task_ThatWasEditedOrIsNotSystems_IsNeverStarted()
    {
        const string system = @"C:\Windows\System32";
        var current = UsbAutoRepairTask.Xml(system);
        Assert.False(UsbAutoRepairTask.IsCurrent(current.Replace("pnputil.exe", "cmd.exe", StringComparison.Ordinal), system));
        Assert.False(UsbAutoRepairTask.IsCurrent(current.Replace(UsbAutoRepairTask.SystemSid, "S-1-5-21-1", StringComparison.Ordinal), system));
        Assert.False(UsbAutoRepairTask.IsCurrent(current.Replace("/scan-devices", "/delete-driver oem1.inf", StringComparison.Ordinal), system));
        Assert.False(UsbAutoRepairTask.IsCurrent(current, @"D:\Elsewhere"));
        Assert.False(UsbAutoRepairTask.IsCurrent("<not xml", system));
        Assert.False(UsbAutoRepairTask.IsCurrent(string.Empty, system));

        // An extra action is as bad as a changed one.
        var extra = XDocument.Parse(current);
        XNamespace ns = extra.Root!.Name.Namespace;
        extra.Root.Element(ns + "Actions")!.Add(new XElement(ns + "Exec", new XElement(ns + "Command", @"C:\Users\x\evil.exe")));
        Assert.False(UsbAutoRepairTask.IsCurrent(extra.ToString(), system));

        var scheduler = new MemoryScheduler { Xml = current.Replace("pnputil.exe", "cmd.exe", StringComparison.Ordinal) };
        var repair = new UsbAutoRepair(scheduler, system);
        Assert.Equal(UsbAutoRepairState.Outdated, repair.Status().State);
        Assert.Throws<InvalidOperationException>(repair.Run);
        Assert.Empty(scheduler.Runs);
    }

    [Fact]
    public void Task_IsSetUpStartedAndRemovedThroughTheScheduler()
    {
        const string system = @"C:\Windows\System32";
        var scheduler = new MemoryScheduler();
        var repair = new UsbAutoRepair(scheduler, system);
        Assert.Equal(UsbAutoRepairState.NotInstalled, repair.Status().State);
        Assert.Equal("not-installed", repair.Status().StateName);
        Assert.Throws<InvalidOperationException>(repair.Run);

        var enabled = repair.Enable();
        Assert.True(enabled.Ready);
        Assert.Equal(("REX", "USB auto-repair", UsbAutoRepairTask.SecurityDescriptor), scheduler.Registered);
        repair.Run();
        Assert.Equal([UsbAutoRepairTask.TaskPath], scheduler.Runs);

        Assert.True(repair.Disable());
        Assert.False(repair.Disable());
        Assert.Equal(UsbAutoRepairState.NotInstalled, repair.Status().State);

        scheduler.Unreadable = true;
        Assert.Equal(UsbAutoRepairState.Unreadable, repair.Status().State);
        Assert.Contains("cannot read", repair.Status().Describe(), StringComparison.Ordinal);

        // A registration that does not read back as asked is reported, not trusted.
        var broken = new UsbAutoRepair(new MemoryScheduler { KeepsNothing = true }, system);
        Assert.Throws<InvalidOperationException>(broken.Enable);
    }

    [Fact]
    public void Status_DescribesEveryState()
    {
        Assert.Contains("not run yet", new UsbAutoRepairStatus(UsbAutoRepairState.Installed).Describe(), StringComparison.Ordinal);
        Assert.Contains("result 0x1", new UsbAutoRepairStatus(UsbAutoRepairState.Installed, T0, 1).Describe(), StringComparison.Ordinal);
        Assert.Equal("outdated", new UsbAutoRepairStatus(UsbAutoRepairState.Outdated).StateName);
        Assert.Equal("unreadable", new UsbAutoRepairStatus(UsbAutoRepairState.Unreadable).StateName);
        Assert.Equal("installed", new UsbAutoRepairStatus(UsbAutoRepairState.Installed).StateName);
    }

    [Fact]
    public async Task RecordingScheduler_PlaysTheElevatedCommandsAndWritesOnlyItsOwnFiles()
    {
        var log = Path.Combine(Path.GetTempPath(), "rex-usb-" + Guid.NewGuid().ToString("N") + ".log");
        try
        {
            var recorder = new RecordingTaskScheduler(log);
            var repair = new UsbAutoRepair(recorder, @"C:\Windows\System32");
            Assert.Equal(0, await recorder.ElevateAsync(["usb", "enable-auto-repair"], @"C:\Windows\System32"));
            Assert.True(repair.Status().Ready);
            repair.Run();
            Assert.Equal(0, await recorder.ElevateAsync(["usb", "disable-auto-repair"], @"C:\Windows\System32"));
            Assert.False(File.Exists(recorder.Store));
            Assert.Equal(
                ["elevate usb enable-auto-repair", $@"register \REX\USB auto-repair {UsbAutoRepairTask.SecurityDescriptor}", @"run \REX\USB auto-repair",
                 "elevate usb disable-auto-repair", @"delete \REX\USB auto-repair"],
                File.ReadAllLines(log));
        }
        finally
        {
            File.Delete(log);
            File.Delete(log + ".task.xml");
        }
    }

    // ----- The prompted repair -----

    [Fact]
    public async Task Repair_RestartsThenRemovesOnlyWhatIsStillFailing()
    {
        var devices = new MemoryDevices([Failed(), Failed(@"USB\VID_0000&PID_0001\6&2")]);
        var runner = new FakeProcessRunner();
        runner.Respond = args =>
        {
            // The first device comes back after its restart; the second does not.
            if (args[0] == "/restart-device" && args[1].Contains("PID_0002", StringComparison.Ordinal))
            {
                devices.Nodes = devices.Nodes.Where(n => n.InstanceId != args[1]).ToList();
            }

            return new ProcessResult(0, "Device restarted successfully.\n", string.Empty);
        };
        var usb = Fake(devices, runner);
        var log = new List<string>();

        var outcome = await UsbRepair.RunAsync(new UsbRepairPlan(usb.Problems()), usb, log.Add, TestContext.Current.CancellationToken);

        Assert.Equal(
            [["/restart-device", @"USB\VID_0000&PID_0002\5&1F007347&0&5"], ["/restart-device", @"USB\VID_0000&PID_0001\6&2"],
             ["/remove-device", @"USB\VID_0000&PID_0001\6&2"], ["/scan-devices"]],
            runner.Calls.Select(c => c.Arguments));
        Assert.All(runner.Calls, c => Assert.Equal(@"C:\Windows\System32\pnputil.exe", c.FileName));
        Assert.Equal(2, outcome.Restarted.Count);
        Assert.Equal([@"USB\VID_0000&PID_0001\6&2"], outcome.Removed);
        Assert.Empty(outcome.Failures);
        Assert.Contains(log, line => line.Contains("still failing after the restart", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Repair_ReportsARefusalAndStillTriesTheFallback()
    {
        var devices = new MemoryDevices([Failed()]);
        var runner = new FakeProcessRunner { Respond = args => args[0] == "/restart-device"
            ? new ProcessResult(1, "Failed to restart device.\n", string.Empty)
            : new ProcessResult(0, string.Empty, string.Empty) };
        var usb = Fake(devices, runner);

        var outcome = await UsbRepair.RunAsync(new UsbRepairPlan(usb.Problems()), usb, _ => { }, TestContext.Current.CancellationToken);

        Assert.Empty(outcome.Restarted);
        Assert.Single(outcome.Failures);
        Assert.Contains("Failed to restart device.", outcome.Failures[0], StringComparison.Ordinal);
        Assert.Equal([Failed().InstanceId], outcome.Removed);
        Assert.Single(outcome.StillFailing);

        var nothing = await UsbRepair.RunAsync(new UsbRepairPlan([]), Fake(new MemoryDevices([]), new FakeProcessRunner()), _ => { }, TestContext.Current.CancellationToken);
        Assert.Empty(nothing.Restarted);
    }

    [Fact]
    public void RepairPlan_DescribesEveryCallInOrderAndQuotesIds()
    {
        var plan = new UsbRepairPlan(UsbProblems.Classify([Failed()]));
        var lines = plan.Describe(@"C:\Windows\System32\pnputil.exe");
        Assert.Contains(@"  C:\Windows\System32\pnputil.exe /restart-device ""USB\VID_0000&PID_0002\5&1F007347&0&5""", lines);
        Assert.Contains(@"  C:\Windows\System32\pnputil.exe /remove-device ""USB\VID_0000&PID_0002\5&1F007347&0&5""", lines);
        Assert.Equal(@"  C:\Windows\System32\pnputil.exe /scan-devices", lines[^1]);
        Assert.Equal(["No USB device is in a failed state, so nothing would be restarted."], new UsbRepairPlan([]).Describe("pnputil"));
    }

    // ----- rex usb -----

    [Fact]
    public async Task Cli_StatusAndDryRunsSayWhatWouldHappenWithoutChangingAnything()
    {
        using var package = new TestPackage();
        var scheduler = new MemoryScheduler();
        var runner = new FakeProcessRunner();
        var usb = Fake(new MemoryDevices([Failed()]), runner, scheduler);
        var context = new CliContext(package.Paths, usb);

        var status = await Machine(context, "usb", "status");
        Assert.Equal("not-installed", status["autoRepair"]!["state"]!.GetValue<string>());
        var problem = status["problems"]![0]!;
        Assert.Equal("failed-enumeration", problem["kind"]!.GetValue<string>());
        Assert.True(problem["autoRepairable"]!.GetValue<bool>());
        Assert.Equal("USB\\VID_0000&PID_0002\\5&1F007347&0&5", problem["instanceId"]!.GetValue<string>());

        var repair = await Machine(context, "usb", "repair", "--dry-run");
        Assert.True(repair["dryRun"]!.GetValue<bool>());
        Assert.Single(repair["restart"]!.AsArray());
        Assert.Equal(2, repair["fallback"]!.AsArray().Count);

        var enable = await Machine(context, "usb", "enable-auto-repair", "--dry-run");
        Assert.Equal(UsbAutoRepairTask.SystemSid, enable["runsAs"]!.GetValue<string>());
        Assert.Equal(@"C:\Windows\System32\pnputil.exe", enable["command"]!.GetValue<string>());
        Assert.Equal(UsbAutoRepairTask.Arguments.Count, enable["arguments"]!.AsArray().Count);

        var disable = await Machine(context, "usb", "disable-auto-repair", "--dry-run");
        Assert.False(disable["removed"]!.GetValue<bool>());

        Assert.Empty(runner.Calls);
        Assert.Null(scheduler.Xml);
        Assert.Contains("run-auto-repair", (await Machine(context, "capabilities"))["usb"]!.AsArray().Select(v => v!.GetValue<string>()));
    }

    [Fact]
    public async Task Cli_MachineModeNeverPromptsAndNeedsAnElevatedShell()
    {
        using var package = new TestPackage();
        var scheduler = new MemoryScheduler();
        var runner = new FakeProcessRunner();
        var elevated = false;
        var usb = Fake(new MemoryDevices([Failed()]), runner, scheduler, () => elevated);
        var context = new CliContext(package.Paths, usb);

        foreach (var verb in new[] { "repair", "enable-auto-repair" })
        {
            var refused = await MachineMode.RunAsync(["usb", verb], context);
            Assert.Equal(1, refused.ExitCode);
            Assert.Contains("administrator shell", JsonNode.Parse(refused.Json)!["error"]!["message"]!.GetValue<string>(), StringComparison.Ordinal);
        }

        var notSetUp = await MachineMode.RunAsync(["usb", "run-auto-repair"], context);
        Assert.Equal(1, notSetUp.ExitCode);
        Assert.Empty(runner.Calls);
        Assert.Null(scheduler.Xml);

        elevated = true;
        Assert.Equal("installed", (await Machine(context, "usb", "enable-auto-repair"))["state"]!.GetValue<string>());
        Assert.True((await Machine(context, "usb", "run-auto-repair"))["started"]!.GetValue<bool>());
        Assert.Equal([UsbAutoRepairTask.TaskPath], scheduler.Runs);

        var repaired = await Machine(context, "usb", "repair");
        Assert.Contains(runner.Calls, c => c.Arguments[0] == "/restart-device");
        Assert.Empty(repaired["failures"]!.AsArray());

        var removed = await Machine(context, "usb", "disable-auto-repair");
        Assert.True(removed["removed"]!.GetValue<bool>());
        Assert.Equal("not-installed", removed["state"]!.GetValue<string>());
    }

    private static async Task<JsonNode> Machine(CliContext context, params string[] args)
    {
        var result = await MachineMode.RunAsync(args, context);
        var doc = JsonNode.Parse(result.Json)!;
        Assert.True(doc["ok"]!.GetValue<bool>(), result.Json);
        return doc["data"]!;
    }

    private static (UsbNotice, bool, int) Tuple(UsbRecoveryDecision decision) => (decision.Notice, decision.Trigger, decision.Attempts);

    private static UsbSystem Fake(MemoryDevices devices, FakeProcessRunner runner, MemoryScheduler? scheduler = null, Func<bool>? elevated = null) => new()
    {
        Devices = devices,
        Scheduler = scheduler ?? new MemoryScheduler(),
        Runner = runner,
        IsElevated = elevated ?? (() => false),
        Elevate = (_, _) => throw new InvalidOperationException("No prompt in tests."),
        AdbInterfaces = () => [],
        RegisterAdbInterfaces = (_, _) => Task.FromResult<IReadOnlyList<string>>([]),
        SystemDirectory = @"C:\Windows\System32",
        Delay = (_, _) => Task.CompletedTask,
    };

    private sealed class MemoryDevices(List<UsbDeviceNode> nodes) : IUsbDeviceSource
    {
        public List<UsbDeviceNode> Nodes { get; set; } = nodes;

        public IReadOnlyList<UsbDeviceNode> ProblemNodes() => Nodes.ToArray();
    }

    private sealed class MemoryScheduler : ITaskScheduler
    {
        public string? Xml { get; set; }
        public bool Unreadable { get; set; }
        public bool KeepsNothing { get; init; }
        public (string Folder, string Name, string Sd)? Registered { get; private set; }
        public List<string> Runs { get; } = [];

        public ScheduledTaskInfo? Find(string path) =>
            Unreadable ? throw new UnauthorizedAccessException() : Xml is null ? null : new ScheduledTaskInfo(Xml, null, null);

        public void Register(string folder, string name, string xml, string securityDescriptor)
        {
            Registered = (folder, name, securityDescriptor);
            Xml = KeepsNothing ? null : xml;
        }

        public bool Delete(string folder, string name)
        {
            var existed = Xml is not null;
            Xml = null;
            return existed;
        }

        public void Run(string path) => Runs.Add(path);
    }
}
