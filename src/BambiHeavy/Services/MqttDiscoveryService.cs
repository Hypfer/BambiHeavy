using System.Text.Json;
using BambiHeavy.Models;
using MQTTnet;

namespace BambiHeavy.Services;

public class MqttDiscoveryService
{
    private const string GetDevicesTopic = "zigbee2mqtt/bridge/request/devices/get";
    private const string DevicesResponseTopic = "zigbee2mqtt/bridge/devices";
    private const string SupportedVendorName = "Philips";

    public event Action<IReadOnlyList<LightMapping>>? LightsDiscovered;

    public async Task<bool> TestConnectionAsync(
        string brokerUrl, int brokerPort, string? username = null, string? password = null, bool useTls = false,
        string? clientId = null, CancellationToken ct = default)
    {
        var factory = new MqttClientFactory();
        var client = factory.CreateMqttClient();

        var optionsBuilder = new MqttClientOptionsBuilder()
            .WithCleanSession()
            .WithTcpServer(brokerUrl, brokerPort);

        if (useTls)
            optionsBuilder.WithTlsOptions(o => o.UseTls());

        if (!string.IsNullOrEmpty(username))
            optionsBuilder.WithCredentials(username, password);

        if (!string.IsNullOrEmpty(clientId))
            optionsBuilder.WithClientId(clientId);
        else
            optionsBuilder.WithClientId($"bambiheavy-test-{Guid.NewGuid():N}");

        var options = optionsBuilder.Build();

        try
        {
            var result = await client.ConnectAsync(options, ct);
            if (result.ResultCode == MqttClientConnectResultCode.Success)
            {
                await client.DisconnectAsync();
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<LightMapping>> DiscoverLightsAsync(
        string brokerUrl, int brokerPort, string? username = null, string? password = null, bool useTls = false,
        string? clientId = null, CancellationToken ct = default)
    {
        var factory = new MqttClientFactory();
        var client = factory.CreateMqttClient();

        var optionsBuilder = new MqttClientOptionsBuilder()
            .WithCleanSession()
            .WithTcpServer(brokerUrl, brokerPort);

        if (useTls)
            optionsBuilder.WithTlsOptions(o => o.UseTls());

        if (!string.IsNullOrEmpty(username))
            optionsBuilder.WithCredentials(username, password);

        if (!string.IsNullOrEmpty(clientId))
            optionsBuilder.WithClientId(clientId);

        var options = optionsBuilder.Build();

        var connectResult = await client.ConnectAsync(options, ct);
        if (connectResult.ResultCode != MqttClientConnectResultCode.Success)
            throw new InvalidOperationException($"MQTT connect failed: {connectResult.ResultCode}");

        var responseTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));

        var messageHandled = false;
        client.ApplicationMessageReceivedAsync += e =>
        {
            if (e.ApplicationMessage.Topic == DevicesResponseTopic && !messageHandled)
            {
                messageHandled = true;
                var payload = e.ApplicationMessage.ConvertPayloadToString();
                responseTcs.TrySetResult(payload);
            }

            return Task.CompletedTask;
        };

        await client.SubscribeAsync(
            new MqttClientSubscribeOptionsBuilder()
                .WithTopicFilter(DevicesResponseTopic)
                .Build(), ct);

        var requestMessage = new MqttApplicationMessageBuilder()
            .WithTopic(GetDevicesTopic)
            .WithPayload("")
            .Build();
        await client.PublishAsync(requestMessage, ct);

        try
        {
            var payload = await responseTcs.Task.WaitAsync(timeoutCts.Token);
            var lights = ParseDiscoveredDevices(payload);
            LightsDiscovered?.Invoke(lights);
            return lights;
        }
        finally
        {
            timeoutCts.Dispose();
            await client.UnsubscribeAsync(DevicesResponseTopic);
            await client.DisconnectAsync();
        }
    }

    private static IReadOnlyList<LightMapping> ParseDiscoveredDevices(string json)
    {
        var results = new List<LightMapping>();

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.ValueKind != JsonValueKind.Array)
            return results;

        foreach (var device in root.EnumerateArray())
        {
            if (!device.TryGetProperty("definition", out var definition))
                continue;

            if (!definition.TryGetProperty("vendor", out var vendor))
                continue;

            if (vendor.GetString() != SupportedVendorName)
                continue;

            var friendlyName = device.TryGetProperty("friendly_name", out var fn)
                ? fn.GetString() ?? ""
                : "";

            var shortAddress = device.TryGetProperty("network_address", out var na)
                ? (ushort)na.GetInt32()
                : (ushort)0;

            var ieeeAddress = device.TryGetProperty("ieee_address", out var ieee)
                ? ieee.GetString()
                : null;

            var modelId = device.TryGetProperty("model_id", out var mid)
                ? mid.GetString()
                : null;

            results.Add(new LightMapping
            {
                FriendlyName = friendlyName,
                ShortAddress = shortAddress,
                IeeeAddress = ieeeAddress,
                ModelId = modelId,
                Zone = "",
                Brightness = 1.0
            });
        }

        return results;
    }
}