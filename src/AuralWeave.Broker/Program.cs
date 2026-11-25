using AuralWeave.Audio.Windows;

if (args.Length != 1 || args[0] != "--mock")
{
    Console.Error.WriteLine(
        "AuralWeave.Broker is a developer-only, non-elevated MOCK PCM broker. " +
        "Use --mock. No kernel driver or Windows service is installed.");
    return 2;
}

using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    stop.Cancel();
};

try
{
    using var broker = new LocalVirtualAudioBroker();
    Console.WriteLine("AURALWEAVE_MOCK_BROKER_STARTING");
    Console.Out.Flush();
    await broker.RunAsync(stop.Token);
    Console.WriteLine("AURALWEAVE_MOCK_BROKER_STOPPED");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine("AURALWEAVE_MOCK_BROKER_ERROR: " + ex.Message);
    return 1;
}
