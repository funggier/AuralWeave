using AuralWeave.Audio.Windows;
using AuralWeave.Core;

try
{
    Console.OutputEncoding = System.Text.Encoding.UTF8;
    Console.WriteLine("AuralWeave diagnostics — Native C++ WASAPI only");

    if (args.Length == 2 && args[0] == "--bridge-owner-child")
    {
        var role = uint.Parse(args[1]);
        using var owned = NativeVirtualAudioBridge.Create(role, 1, 48000, 2, 512);
        Console.WriteLine("OWNER_MAP=" + owned.Name);
        Console.Out.Flush();
        await Task.Delay(Timeout.InfiniteTimeSpan);
        return 0;
    }

    if (args.Contains("--test-virtual-bus-recovery"))
    {
        await VirtualBusRecoveryTests.RunAsync();
        return 0;
    }

    if (args.Contains("--test-local-broker"))
    {
        await LocalBrokerIntegrationTests.RunAsync();
        return 0;
    }

    if (args.Contains("--test-kernel-pcm-client"))
    {
        KernelPcmControlTests.Run();
        return 0;
    }

    if (args.Contains("--test-kernel-pcm-worker"))
    {
        await KernelPcmControlTests.RunWorkerAsync();
        return 0;
    }

    if (args.Contains("--probe-virtual-input-lease") ||
        args.Contains("--probe-virtual-output-lease"))
    {
        // Called by a second dotnet process, independent of folder location.
        var role = args.Contains("--probe-virtual-input-lease")
            ? VirtualAudioEndpointRole.Input : VirtualAudioEndpointRole.Output;
        using var lease = VirtualDeviceControlLease.TryAcquire(role);
        Console.WriteLine(lease is null ? $"BUSY: Virtual {role}" : $"AVAILABLE: Virtual {role}");
        return lease is null ? 17 : 0;
    }

    if (args.Contains("--test-virtual-device-ownership"))
    {
        int Probe(VirtualAudioEndpointRole role)
        {
            var dll = Environment.GetEnvironmentVariable("AW_LEASE_PROBE_DLL")
                ?? Path.Combine(AppContext.BaseDirectory, "AuralWeave.Diagnostics.dll");
            var command = new System.Diagnostics.ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            command.ArgumentList.Add(dll);
            command.ArgumentList.Add(role == VirtualAudioEndpointRole.Input
                ? "--probe-virtual-input-lease" : "--probe-virtual-output-lease");
            using var child = System.Diagnostics.Process.Start(command)
                ?? throw new InvalidOperationException("Could not start second process.");
            if (!child.WaitForExit(12000))
            {
                child.Kill(entireProcessTree: true);
                throw new InvalidOperationException("Child process did not exit.");
            }
            var result = child.ExitCode;
            var output = child.StandardOutput.ReadToEnd();
            var errors = child.StandardError.ReadToEnd();
            Console.WriteLine($"Child {role}: {result}, {output.Trim()} {errors.Trim()}");
            return result;
        }

        using var inputControl = VirtualDeviceControlLease.TryAcquire(
            VirtualAudioEndpointRole.Input)
            ?? throw new InvalidOperationException("Cannot acquire first Virtual Input.");
        if (VirtualDeviceControlLease.TryAcquire(VirtualAudioEndpointRole.Input) is not null ||
            Probe(VirtualAudioEndpointRole.Input) != 17)
            throw new InvalidOperationException("Virtual Input was concurrently acquired.");
        Console.WriteLine("PASS: no duplicate Virtual Input controller, even in another process.");

        if (Probe(VirtualAudioEndpointRole.Output) != 0)
            throw new InvalidOperationException("Input lock blocked independent Virtual Output.");
        Console.WriteLine("PASS: second process can independently control Virtual Output.");

        using (var outputControl = VirtualDeviceControlLease.TryAcquire(
            VirtualAudioEndpointRole.Output)
            ?? throw new InvalidOperationException("Cannot acquire Virtual Output."))
        {
            if (Probe(VirtualAudioEndpointRole.Output) != 17)
                throw new InvalidOperationException("Virtual Output cross-process lock failed.");
            Console.WriteLine("PASS: no duplicate Virtual Output controller.");
        }
        if (Probe(VirtualAudioEndpointRole.Output) != 0)
            throw new InvalidOperationException("Virtual Output lease was not released.");
        Console.WriteLine("PASS: released Virtual Output can be acquired by other process.");

        // Owning thread is dedicated, so async continuations can dispose
        // from a different thread without mutex ownership exceptions.
        await Task.Yield();
        inputControl.Dispose();
        if (Probe(VirtualAudioEndpointRole.Input) != 0)
            throw new InvalidOperationException("Virtual Input lease was not released.");
        Console.WriteLine("PASS: async owner release enables another process to take over.");

        Console.WriteLine("VIRTUAL_DRIVER_CROSS_PROCESS_OWNERSHIP=PASS");
        return 0;
    }

    if (args.Length == 3 && args[0] == "--probe-shared-audio-active")
    {
        var role = uint.Parse(args[1]) == 1
            ? VirtualAudioEndpointRole.Input : VirtualAudioEndpointRole.Output;
        var expected = bool.Parse(args[2]);
        var actual = SharedVirtualAudioActivityLease.IsActive(role);
        if (actual != expected)
            throw new InvalidOperationException(
                $"Audio lifetime guard mismatch for {role}: expected {expected} got {actual}.");
        Console.WriteLine($"PASS: cross-process shared audio role {role}, active={actual}");
        return 0;
    }

    if (args.Length == 5 && args[0] == "--shared-bus-producer")
    {
        // Separate Portable copy writes to its own SPSC mapping.
        var mappingName = args[1];
        var role = uint.Parse(args[2]);
        var amplitude = int.Parse(args[3]) / 100f;
        var expectedFrames = int.Parse(args[4]);
        using var peer = NativeVirtualAudioBridge.Attach(mappingName, role, 1);
        var block = Enumerable.Repeat(amplitude,
            checked(expectedFrames * (int)peer.Channels)).ToArray();
        if (peer.Write(block) != (uint)expectedFrames)
            throw new InvalidOperationException("Cross-folder PCM producer could not write its own stream.");
        Console.WriteLine($"SHARED_PCM_PRODUCER_PASS {role} {amplitude}");
        return 0;
    }
    if (args.Length == 5 && args[0] == "--shared-bus-consumer")
    {
        // Each subscriber reads an independent SPSC mapping, not a shared
        // read cursor that could steal frames from another Portable copy.
        var name = args[1];
        var role = uint.Parse(args[2]);
        var expected = int.Parse(args[3]) / 100f;
        var expectedFrames = int.Parse(args[4]);
        using var peer = NativeVirtualAudioBridge.Attach(name, role, 1);
        var buffer = new float[checked(expectedFrames * (int)peer.Channels)];
        if (peer.Read(buffer) != expectedFrames ||
            buffer.Any(v => Math.Abs(v - expected) > 0.0001f))
            throw new InvalidOperationException("Cross-folder PCM fanout samples differ.");
        Console.WriteLine($"SHARED_PCM_CONSUMER_PASS {role} {expected} {expectedFrames}");
        return 0;
    }
    if (args.Contains("--test-shared-virtual-audio"))
    {
        int Probe(string dll, params string[] arguments)
        {
            var command = new System.Diagnostics.ProcessStartInfo("dotnet")
            {
                UseShellExecute = false, RedirectStandardOutput = true,
                RedirectStandardError = true, CreateNoWindow = true
            };
            command.ArgumentList.Add(dll);
            foreach (var argument in arguments) command.ArgumentList.Add(argument);
            using var child = System.Diagnostics.Process.Start(command)
                ?? throw new InvalidOperationException("Unable to start portable PCM client.");
            if (!child.WaitForExit(12000))
            {
                child.Kill(entireProcessTree: true);
                throw new TimeoutException("Portable PCM client did not complete.");
            }
            var log = child.StandardOutput.ReadToEnd();
            if (child.ExitCode != 0)
                throw new InvalidOperationException($"Client exit {child.ExitCode}: " +
                    log + child.StandardError.ReadToEnd());
            Console.WriteLine(log.Trim());
            return child.ExitCode;
        }

        var local = Path.Combine(AppContext.BaseDirectory, "AuralWeave.Diagnostics.dll");
        var alternative = Environment.GetEnvironmentVariable("AW_SHARED_BUS_PROBE_DLL")
            ?? local;
        Console.WriteLine("Shared-bus client A: " + local);
        Console.WriteLine("Shared-bus client B: " + alternative);
        foreach (var role in new[] { 1u, 2u })
        {
            using var sourceA = NativeVirtualAudioBridge.Create(role, 1, 48000, 2, 512);
            using var sourceB = NativeVirtualAudioBridge.Create(role, 1, 48000, 2, 512);
            using var receiveA = NativeVirtualAudioBridge.Create(role, 1, 48000, 2, 512);
            using var receiveB = NativeVirtualAudioBridge.Create(role, 1, 48000, 2, 512);
            using var hub = new SharedVirtualAudioBus(role, 48000, 2, 256);
            var endpointRole = role == 1u
                ? VirtualAudioEndpointRole.Input : VirtualAudioEndpointRole.Output;
            if (!SharedVirtualAudioActivityLease.IsActive(endpointRole))
                throw new InvalidOperationException("Audio owner activity guard missing.");
            Probe(alternative, "--probe-shared-audio-active", $"{role}", "true");
            hub.AddContributor(sourceA.Name);
            hub.AddContributor(sourceB.Name);
            hub.AddListener(receiveA.Name);
            hub.AddListener(receiveB.Name);
            Console.WriteLine($"Testing role {role}: {hub.Contributors} independent sources and {hub.Listeners} listeners.");
            // Writer A and B run in different folder copies. Their streams
            // are mixed by a single logical device (0.25+0.40 = 0.65).
            Probe(local, "--shared-bus-producer", sourceA.Name, $"{role}", "25", "256");
            Probe(alternative, "--shared-bus-producer", sourceB.Name, $"{role}", "40", "256");
            if (hub.Pump() != 256 || hub.FramesDelivered != 512 ||
                hub.FramesDropped != 0)
                throw new InvalidOperationException("Two-user mixer did not publish both streams.");
            Probe(local, "--shared-bus-consumer", receiveA.Name, $"{role}", "65", "256");
            Probe(alternative, "--shared-bus-consumer", receiveB.Name, $"{role}", "65", "256");
            Console.WriteLine($"PASS: role {role}: two Portable clients hear identical 0.65 mixed PCM.");

            // After one contributor leaves, the other still works.
            if (!hub.RemoveContributor(sourceA.Name))
                throw new InvalidOperationException("Could not detach previous source.");
            Probe(alternative, "--shared-bus-producer", sourceB.Name, $"{role}", "20", "256");
            if (hub.Pump() != 256)
                throw new InvalidOperationException("Remaining producer failed.");
            Probe(local, "--shared-bus-consumer", receiveA.Name, $"{role}", "20", "256");
            Probe(alternative, "--shared-bus-consumer", receiveB.Name, $"{role}", "20", "256");
            Console.WriteLine($"PASS: role {role}: remaining copy continues independently.");

            // Short final blocks must preserve only real frames and avoid
            // writing the stale tail of a preallocated mixer buffer.
            Probe(alternative, "--shared-bus-producer", sourceB.Name, $"{role}", "80", "37");
            if (hub.Pump() != 37)
                throw new InvalidOperationException("Short PCM block was not processed.");
            Probe(local, "--shared-bus-consumer", receiveA.Name, $"{role}", "80", "37");
            Probe(alternative, "--shared-bus-consumer", receiveB.Name, $"{role}", "80", "37");
            Console.WriteLine($"PASS: role {role}: exact 37-frame partial PCM block.");

            // Mix clipping is intentionally bounded; PCM never wraps,
            // and each listener receives precisely the same clamped value.
            hub.AddContributor(sourceA.Name);
            Probe(local, "--shared-bus-producer", sourceA.Name, $"{role}", "80", "256");
            Probe(alternative, "--shared-bus-producer", sourceB.Name, $"{role}", "80", "256");
            if (hub.Pump() != 256)
                throw new InvalidOperationException("Clamped multi-writer PCM block failed.");
            Probe(local, "--shared-bus-consumer", receiveA.Name, $"{role}", "100", "256");
            Probe(alternative, "--shared-bus-consumer", receiveB.Name, $"{role}", "100", "256");
            Console.WriteLine($"PASS: role {role}: multiple contributions clamp safely at 1.0.");

            // Simulate a slow receiver. It must not steal samples from
            // the fast receiver, or stall the mixer and other clients.
            for (var cycle = 0; cycle < 3; cycle++)
            {
                Probe(alternative, "--shared-bus-producer", sourceB.Name, $"{role}", "25", "256");
                if (hub.Pump() != 256)
                    throw new InvalidOperationException("Slow subscriber blocked PCM mixing.");
                Probe(alternative, "--shared-bus-consumer", receiveB.Name, $"{role}", "25", "256");
                // receiveA intentionally does not consume three blocks;
                // its 512-frame buffer fills after two blocks.
            }
            if (hub.FramesDropped < 256)
                throw new InvalidOperationException("Slow listener overflow not counted.");
            Console.WriteLine($"PASS: role {role}: slow listener dropped {hub.FramesDropped} frames " +
                "but independent listener received all three blocks.");
        }
        if (SharedVirtualAudioActivityLease.IsActive(VirtualAudioEndpointRole.Input) ||
            SharedVirtualAudioActivityLease.IsActive(VirtualAudioEndpointRole.Output))
            throw new InvalidOperationException(
                "An audio role is still marked active after all owners closed.");
        Probe(alternative, "--probe-shared-audio-active", "1", "false");
        Probe(alternative, "--probe-shared-audio-active", "2", "false");
        Console.WriteLine("PASS: all user-mode audio owner handles released across copies.");
        Console.WriteLine("MULTI_PORTABLE_SHARED_VIRTUAL_MIX_AND_FANOUT=PASS");
        return 0;
    }

    var sources = NativeAudioDeviceCatalog.CaptureDevices();
    var outputs = NativeAudioDeviceCatalog.RenderDevices();
    Console.WriteLine($"CAPTURE / LOOPBACK SOURCES: {sources.Count}");
    foreach (var source in sources)
        Console.WriteLine($"  [{source.Kind}] {source.DisplayName} | default={source.IsDefault}" +
            $" | {source.SampleRate} Hz / {source.Channels} ch | id={source.Id}");
    Console.WriteLine($"RENDER OUTPUTS: {outputs.Count}");
    foreach (var output in outputs)
        Console.WriteLine($"  {output.DisplayName} | default={output.IsDefault}" +
            $" | {output.SampleRate} Hz / {output.Channels} ch | id={output.Id}");

    if (args.Contains("--test-virtual-driver-discovery"))
    {
        if (!AuralWeaveVirtualDrivers.TryIdentifyRole(
                @"ROOT\AURALWEAVE_VINPUT_0001", out var inputRole) ||
            inputRole != VirtualAudioEndpointRole.Input ||
            !AuralWeaveVirtualDrivers.TryIdentifyRole(
                @"ROOT\AURALWEAVE_VOUTPUT_0001", out var outputRole) ||
            outputRole != VirtualAudioEndpointRole.Output ||
            AuralWeaveVirtualDrivers.TryIdentifyRole(
                @"USB\VID_1234&PID_5678", out _) ||
            AuralWeaveVirtualDrivers.TryIdentifyRole(
                @"ROOT\AURALWEAVE_VINPUTOTHER", out _) ||
            AuralWeaveVirtualDrivers.TryIdentifyRole(
                @"ROOT\AURALWEAVE_VINPUT", out _) ||
            AuralWeaveVirtualDrivers.TryIdentifyRole(
                @"ROOT\AURALWEAVE_VINPUT_0000", out _) ||
            AuralWeaveVirtualDrivers.TryIdentifyRole(
                @"ROOT\AURALWEAVE_VOUTPUT_0017", out _) ||
            AuralWeaveVirtualDrivers.TryIdentifyRole(
                @"ROOT\AURALWEAVE_VOUTPUT_0001&EXTRA", out _) ||
            !AuralWeaveVirtualDrivers.TryIdentifyRole(
                @"root\auralweave_vinput_0001", out var inputOne) ||
            inputOne != VirtualAudioEndpointRole.Input ||
            AuralWeaveVirtualDrivers.TryIdentifyRole(
                @"ROOT\AURALWEAVE_VINPUT_0002", out _) ||
            AuralWeaveVirtualDrivers.TryIdentifyRole(
                @"ROOT\AURALWEAVE_VOUTPUT_0016", out _) ||
            !AuralWeaveVirtualDrivers.TryIdentifyLegacyRole(
                @"ROOT\AURALWEAVE_VINPUT_0002", out var legacyInput) ||
            legacyInput != VirtualAudioEndpointRole.Input ||
            !AuralWeaveVirtualDrivers.TryIdentifyLegacyRole(
                @"ROOT\AURALWEAVE_VOUTPUT_0016", out var legacyOutput) ||
            legacyOutput != VirtualAudioEndpointRole.Output)
            throw new InvalidOperationException(
                "Virtual Driver ownership matching is incorrect.");
        if (AuralWeaveVirtualDrivers.MaxVirtualInputs != 1 ||
            AuralWeaveVirtualDrivers.MaxVirtualOutputs != 1 ||
            !AuralWeaveVirtualDrivers.MayInstall([], VirtualAudioEndpointRole.Input) ||
            !AuralWeaveVirtualDrivers.MayInstall([], VirtualAudioEndpointRole.Output))
            throw new InvalidOperationException("Single-endpoint capacity contract is invalid.");
        var simulatedInstalled = new[] {
            new AuralWeaveVirtualDevice(@"ROOT\AURALWEAVE_VINPUT_0001", "VInput",
                VirtualAudioEndpointRole.Input, AuralWeaveVirtualDrivers.InputHardwareId),
            new AuralWeaveVirtualDevice(@"ROOT\AURALWEAVE_VOUTPUT_0001", "VOutput",
                VirtualAudioEndpointRole.Output, AuralWeaveVirtualDrivers.OutputHardwareId)
        };
        if (AuralWeaveVirtualDrivers.MayInstall(simulatedInstalled, VirtualAudioEndpointRole.Input) ||
            AuralWeaveVirtualDrivers.MayInstall(simulatedInstalled, VirtualAudioEndpointRole.Output))
            throw new InvalidOperationException("Duplicate virtual driver installation must be blocked.");
        var legacyInstalled = new[] {
            new AuralWeaveVirtualDevice(@"ROOT\AURALWEAVE_VINPUT_0002", "Old VInput",
                VirtualAudioEndpointRole.Input, @"ROOT\AURALWEAVE_VINPUT_0002")
        };
        if (!legacyInstalled[0].IsLegacy ||
            AuralWeaveVirtualDrivers.MayInstall(legacyInstalled, VirtualAudioEndpointRole.Input))
            throw new InvalidOperationException("Legacy device must be discoverable and block installation.");
        Console.WriteLine("PASS: one current PnP ID per role, duplicate install guard and legacy detection.");

        // The install-only UI must NEVER create another PnP node if a
        // current, legacy or half-registered Windows device ID already
        // exists. The unsigned development prototype is not installable.
        var halfInstalled = new[] {
            new AuralWeaveVirtualDevice(
                @"ROOT\AURALWEAVE_VINPUT_0001", "Half-installed mic",
                VirtualAudioEndpointRole.Input,
                AuralWeaveVirtualDrivers.InputHardwareId,
                IsDriverBound: false)
        };
        var current = VirtualDriverInstallation.Check(
            simulatedInstalled, VirtualAudioEndpointRole.Input);
        var obsolete = VirtualDriverInstallation.Check(
            legacyInstalled, VirtualAudioEndpointRole.Input);
        var incomplete = VirtualDriverInstallation.Check(
            halfInstalled, VirtualAudioEndpointRole.Input);
        if (current.CanInstall || obsolete.CanInstall || incomplete.CanInstall ||
            !current.Description.Contains("installed", StringComparison.OrdinalIgnoreCase) ||
            !obsolete.Description.Contains("obsolete", StringComparison.OrdinalIgnoreCase) ||
            !incomplete.Description.Contains("not fully", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Signed installer readiness must reject existing root devices.");

        var noBundleFolder = Path.Combine(AppContext.BaseDirectory, "drivers", "AuralWeaveVirtualAudio");
        if (!Directory.Exists(noBundleFolder))
        {
            var missing = VirtualDriverInstallation.Check([], VirtualAudioEndpointRole.Output);
            if (missing.CanInstall || !missing.Description.Contains(
                    "not yet included", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Unsigned/missing production package was enabled.");
        }
        Console.WriteLine("PASS: install-only eligibility denies existing/legacy/partly installed PnP IDs and missing package.");
        var installed = AuralWeaveVirtualDrivers.EnumerateInstalled();
        Console.WriteLine($"PASS: Windows SetupAPI discovery; {installed.Count} owned devices found.");
        foreach (var device in installed)
            Console.WriteLine($"  [{device.Role}] {device.Name} ({device.InstanceId})");
        Console.WriteLine("PASS: unrelated PnP devices never match AuralWeave namespace.");
    }
    if (args.Contains("--smoke-virtual-bridge"))
    {
        // Safe real-WASAPI capture test: muted physical output, optional
        // user-mode shared PCM tap. NEVER installs a Windows driver.
        var input = sources.FirstOrDefault(x => x.Kind == AudioEndpointKind.Capture && x.IsDefault)
            ?? sources.FirstOrDefault(x => x.Kind == AudioEndpointKind.Capture)
            ?? throw new InvalidOperationException("No usable capture endpoint.");
        var output = outputs.FirstOrDefault(x => x.IsDefault)
            ?? outputs.FirstOrDefault()
            ?? throw new InvalidOperationException("No usable render endpoint.");
        Console.WriteLine("VIRTUAL PCM TAP: native physical capture -> filters -> user-mode bridge; output muted.");
        using var route = new NativeWasapiAudioRoute();
        using var owner = NativeVirtualAudioBridge.Create(1, 1, 48000, 2, 4096);
        using var peer = NativeVirtualAudioBridge.Attach(owner.Name, 1, 1);
        route.ReplaceFilterChain([
            new FilterStage(Guid.NewGuid(), FilterKind.Gain, true, 0.75f)
        ]);
        route.Start(input.Id, [new OutputRoute(Guid.NewGuid(), output.Id, 1f, true)]);
        try
        {
            route.BindVirtualInputBridge(owner);
            // Consume continuously; avoid intentionally saturating the
            // bounded ring before beginning playback.
            var buffer = new float[1920]; // 960 stereo frames per read
            ulong framesReceived = 0;
            var end = DateTime.UtcNow.AddMilliseconds(1200);
            while (DateTime.UtcNow < end)
            {
                framesReceived += peer.Read(buffer);
                await Task.Delay(5);
            }
            var stats = owner.GetStats();
            var captures = route.CapturedFrames;
            Console.WriteLine($"Capture frames: {captures}; bridge wrote: {stats.FramesWritten}; " +
                $"bridge read: {framesReceived}; dropped: {stats.OverflowFrames}; " +
                $"underruns: {stats.UnderflowFrames}; rate: {stats.SampleRate}; " +
                $"channels: {stats.Channels}");
            if (captures <= 0 || stats.FramesWritten == 0 || framesReceived == 0 ||
                stats.SampleRate != 48000 || stats.Channels != 2 || route.LastError is not null)
                throw new InvalidOperationException("Post-filter Virtual Input PCM tap did not stream.");
            Console.WriteLine("PASS: WASAPI capture -> active native Filter Chain -> shared PCM tap -> independent consumer.");
            route.UnbindVirtualInputBridge();
        }
        finally
        {
            route.Stop();
        }
    }

    if (args.Contains("--smoke-virtual-output"))
    {
        // This creates TWO user-mode Shared Memory mappings and opens only
        // a muted physical playback device. No kernel driver is installed.
        var output = outputs.FirstOrDefault(x => x.IsDefault)
            ?? outputs.FirstOrDefault()
            ?? throw new InvalidOperationException("No physical render endpoint available.");
        Console.WriteLine("VIRTUAL OUTPUT ROUTE TEST: simulated output PCM -> Studio Gain Filter -> virtual input PCM (muted physical output).");
        using var virtualOutput = NativeVirtualAudioBridge.Create(2, 1, 48000, 2, 4096);
        using var virtualInput = NativeVirtualAudioBridge.Create(1, 1, 48000, 2, 4096);
        using var virtualMicReader = NativeVirtualAudioBridge.Attach(virtualInput.Name, 1, 1);
        using var route = new NativeWasapiAudioRoute();
        route.ReplaceFilterChain([
            new FilterStage(Guid.NewGuid(), FilterKind.Gain, true, 0.5f)
        ]);
        route.Start("virtual-output:" + virtualOutput.Name,
            [new OutputRoute(Guid.NewGuid(), output.Id, 1f, true)]);
        try
        {
            route.BindVirtualInputBridge(virtualInput);
            var inputPcm = Enumerable.Repeat(0.2f, 480 * 2).ToArray();
            var readerPcm = new float[960 * 2];
            ulong wrote = 0, received = 0, exactGainSamples = 0;
            for (var i = 0; i < 120; i++)
            {
                wrote += virtualOutput.Write(inputPcm);
                await Task.Delay(10);
                var frames = virtualMicReader.Read(readerPcm);
                received += frames;
                for (var j = 0; j < (int)frames * 2; j++)
                {
                    if (Math.Abs(readerPcm[j] - 0.1f) < 0.0001f)
                        exactGainSamples++;
                }
            }
            var fromVirtual = route.CapturedFrames;
            var statsIn = virtualInput.GetStats();
            var statsOut = virtualOutput.GetStats();
            Console.WriteLine($"Virtual Output source wrote {wrote}, Native Studio received {fromVirtual}, " +
                $"Virtual Input wrote {statsIn.FramesWritten}, read {received}, " +
                $"Gain-processed samples {exactGainSamples}, Output dropped {statsOut.OverflowFrames}");
            if (wrote < 24000 || fromVirtual < 24000 || received < 10000 ||
                exactGainSamples < 10000 || route.LastError is not null)
                throw new InvalidOperationException("Virtual Output source -> native filters -> Virtual Input tap failed.");
            Console.WriteLine("PASS: Virtual Output (user mode) -> native Studio filters -> Virtual Input (user mode).");
            // A closed source must terminate the Native route, even if no
            // further PCM arrives. Stop must still be idempotent afterward.
            virtualOutput.Dispose();
            await Task.Delay(100);
            if (route.IsRunning)
                throw new InvalidOperationException("Native route continued running after virtual source closed.");
            Console.WriteLine("PASS: Virtual Output producer shutdown stops Native route cleanly.");
        }
        finally
        {
            route.UnbindVirtualInputBridge();
            route.Stop();
        }
    }

    var single = args.Contains("--smoke-native") || args.Contains("--smoke-silent");
    var mixed = args.Contains("--smoke-native-multi") ||
                args.Contains("--soak-native-multi") ||
                args.Contains("--long-soak-native-multi");
    if (single)
    {
        var input = sources.FirstOrDefault(x => x.Kind == AudioEndpointKind.Capture && x.IsDefault)
            ?? sources.FirstOrDefault(x => x.Kind == AudioEndpointKind.Capture)
            ?? throw new InvalidOperationException("No active capture input.");
        var output = outputs.FirstOrDefault(x => x.IsDefault)
            ?? outputs.FirstOrDefault()
            ?? throw new InvalidOperationException("No active render output.");

        Console.WriteLine($"NATIVE SILENT: {input.Name} -> {output.Name} (muted)");
        using var route = new NativeWasapiAudioRoute();
        route.ReplaceEffects([
            new AudioEffectInstance(Guid.NewGuid(), AudioRouteRules.GainEffect, true, 0.5f),
            new AudioEffectInstance(Guid.NewGuid(), AudioRouteRules.GainEffect, true, 0.5f)
        ]);
        route.Start(input.Id, [new OutputRoute(Guid.NewGuid(), output.Id, 1f, true)]);
        await Task.Delay(1500);
        var frames = route.CapturedFrames;
        var overrun = route.DroppedRenderFrames;
        var underrun = route.UnderrunRenderFrames;
        var error = route.LastError;
        route.Stop();
        Console.WriteLine($"Native frames: {frames}; overrun: {overrun}; underrun: {underrun}; error: {error ?? "none"}");
        if (frames <= 0 || error is not null)
            throw new InvalidOperationException("Native single-output muted smoke failed.");
        Console.WriteLine("PASS: Native muted capture -> render route and Stop.");
    }

    if (args.Contains("--smoke-rnnoise-toggle") || args.Contains("--soak-rnnoise"))
    {
        var input = sources.FirstOrDefault(x => x.Kind == AudioEndpointKind.Capture && x.SampleRate == 192000)
            ?? sources.FirstOrDefault(x => x.Kind == AudioEndpointKind.Capture)
            ?? throw new InvalidOperationException("No physical capture device available.");
        var output1 = outputs.FirstOrDefault(x => x.IsDefault)
            ?? outputs.FirstOrDefault()
            ?? throw new InvalidOperationException("No render endpoints.");
        var output2 = outputs.FirstOrDefault(x => x.Id != output1.Id && x.SampleRate == 48000)
            ?? outputs.FirstOrDefault(x => x.Id != output1.Id);
        var selected = new List<OutputRoute> { new(Guid.NewGuid(), output1.Id, 1f, true) };
        if (output2 is not null)
            selected.Add(new OutputRoute(Guid.NewGuid(), output2.Id, 1f, true));
        Console.WriteLine($"RNNOISE TOGGLE (muted): {input.Name}, {selected.Count} output(s)");
        using var route = new NativeWasapiAudioRoute();
        route.SetNoiseMode(NoiseProcessingMode.Bypass);
        route.Start(input.Id, selected);
        await Task.Delay(1000);
        var before = route.CapturedFrames;
        route.SetNoiseMode(NoiseProcessingMode.RnnoiseStandard);
        await Task.Delay(args.Contains("--soak-rnnoise") ? 10000 : 1800);
        var during = route.CapturedFrames;
        var underrunsAtNoiseEnd = route.UnderrunRenderFrames;
        route.SetNoiseMode(NoiseProcessingMode.Bypass);
        await Task.Delay(800);
        var after = route.CapturedFrames;
        var errors = route.LastError;
        var underruns = route.UnderrunRenderFrames;
        var overruns = route.DroppedRenderFrames;
        route.Stop();
        Console.WriteLine($"Bypass frames: {before}; RNNoise frames: {during-before}; " +
            $"resumed bypass frames: {after-during}; overruns: {overruns}; " +
            $"underruns: {underruns}; after noise: {underruns-underrunsAtNoiseEnd}; " +
            $"error: {errors ?? "none"}");
        if (!(before > 0 && during > before && after > during && errors is null))
            throw new InvalidOperationException("RNNoise live switch diagnostic failed.");
        Console.WriteLine("PASS: Native RNNoise ↔ Bypass live switching, muted endpoints and Stop.");
    }
    if (args.Contains("--smoke-filter-chain"))
    {
        var input = sources.FirstOrDefault(x => x.Kind == AudioEndpointKind.Capture && x.IsDefault)
            ?? sources.First(x => x.Kind == AudioEndpointKind.Capture);
        var out1 = outputs.FirstOrDefault(x => x.IsDefault) ?? outputs.First();
        var out2 = outputs.FirstOrDefault(x => x.Id != out1.Id && x.SampleRate != out1.SampleRate);
        var sinks = new List<OutputRoute> { new(Guid.NewGuid(), out1.Id, 1f, true) };
        if (out2 is not null)
            sinks.Add(new OutputRoute(Guid.NewGuid(), out2.Id, 1f, true));
        Console.WriteLine($"ORDERED FILTERS (muted): {input.DisplayName} → {sinks.Count} outputs");
        using var route = new NativeWasapiAudioRoute();
        route.ReplaceFilterChain([]); // zero selected filters is true bypass
        route.Start(input.Id, sinks);
        await Task.Delay(600);
        var noFilters = route.CapturedFrames;
        route.ReplaceFilterChain([
            new FilterStage(Guid.NewGuid(), FilterKind.Gain, true, .8f),
            new FilterStage(Guid.NewGuid(), FilterKind.NoiseGate, true, -45f),
            new FilterStage(Guid.NewGuid(), FilterKind.RnnoiseStandard, true, 0f),
            new FilterStage(Guid.NewGuid(), FilterKind.Limiter, true, -1f),
            new FilterStage(Guid.NewGuid(), FilterKind.RnnoiseStandard, false, 0f)
        ]);
        await Task.Delay(2400);
        var processed = route.CapturedFrames;
        var errorDuring = route.LastError;
        route.ReplaceFilterChain([
            new FilterStage(Guid.NewGuid(), FilterKind.Limiter, true, -2f),
            new FilterStage(Guid.NewGuid(), FilterKind.RnnoiseStandard, true, 0f),
            new FilterStage(Guid.NewGuid(), FilterKind.Gain, true, .9f)
        ]);
        await Task.Delay(700);
        var reordered = route.CapturedFrames;
        route.ReplaceFilterChain([]); // back to unprocessed signal
        await Task.Delay(600);
        var resumed = route.CapturedFrames;
        var error = route.LastError;
        var overruns = route.DroppedRenderFrames;
        var underruns = route.UnderrunRenderFrames;
        route.Stop();
        Console.WriteLine($"Frames: {noFilters} → {processed} → {reordered} → {resumed}; " +
            $"overruns={overruns}; underruns={underruns}; error={error ?? errorDuring ?? "none"}");
        if (!(noFilters > 0 && processed > noFilters && reordered > processed &&
              resumed > reordered && error is null && errorDuring is null))
            throw new InvalidOperationException("Ordered native filter chain smoke failed.");
        Console.WriteLine("PASS: Native ordered filter chain / reorder / bypass / Stop.");
    }
    if (mixed)
    {
        var input = sources.FirstOrDefault(x => x.Kind == AudioEndpointKind.Capture)
            ?? throw new InvalidOperationException("No active capture input.");
        var first = outputs.FirstOrDefault(x => x.SampleRate == 192000);
        var second = outputs.FirstOrDefault(x => x.SampleRate == 48000);
        if (first is null || second is null)
            throw new InvalidOperationException("Mixed-rate smoke requires 192k and 48k outputs.");
        Console.WriteLine($"NATIVE MULTI MUTED: {input.Name} -> {first.Name} [192k] + {second.Name} [48k]");
        using var route = new NativeWasapiAudioRoute();
        route.ReplaceEffects([
            new AudioEffectInstance(Guid.NewGuid(), AudioRouteRules.GainEffect, true, 0.8f)
        ]);
        route.Start(input.Id, [
            new OutputRoute(Guid.NewGuid(), first.Id, 1f, true),
            new OutputRoute(Guid.NewGuid(), second.Id, 1f, true)
        ]);
        await Task.Delay(2000);
        var warmupOverrun = route.DroppedRenderFrames;
        var warmupUnderrun = route.UnderrunRenderFrames;
        var duration = args.Contains("--long-soak-native-multi") ? 28000
            : args.Contains("--soak-native-multi") ? 8000 : 500;
        await Task.Delay(duration);
        var frames = route.CapturedFrames;
        var overruns = route.DroppedRenderFrames;
        var underruns = route.UnderrunRenderFrames;
        var error = route.LastError;
        route.Stop();
        Console.WriteLine($"Frames: {frames}; overruns: {overruns}; underruns: {underruns};" +
            $" post-warmup overrun delta: {overruns - warmupOverrun};" +
            $" post-warmup underrun delta: {underruns - warmupUnderrun};" +
            $" error: {error ?? "none"}");
        if (frames <= 0 || error is not null)
            throw new InvalidOperationException("Native mixed-rate muted smoke failed.");
        Console.WriteLine("PASS: Native muted mixed-rate multi-output and Stop.");
    }
}
catch (Exception error)
{
    Console.Error.WriteLine("Diagnostic failed: " + error);
    return 1;
}
return 0;
