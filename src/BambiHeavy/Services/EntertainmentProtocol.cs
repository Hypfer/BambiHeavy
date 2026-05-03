using System.Buffers.Binary;
using System.Text.Json;
using BambiHeavy.Models;
using MQTTnet;

namespace BambiHeavy.Services;

public static class EntertainmentProtocol
{
    private const ushort ManufacturerId = 0x100B;
    private const double CieXMax = 0.7347; // GRADIENT_COLORS_MAX_X
    private const double CieYMax = 0.8264; // GRADIENT_COLORS_MAX_Y

    public static async Task SendEntertainmentChunk(IMqttClient client, Light proxy, List<LightState> states,
        ushort smoothing, uint counter)
    {
        var payloadLen = 6 + states.Count * 7;
        var payloadBytes = new byte[payloadLen];

        BinaryPrimitives.WriteUInt32LittleEndian(payloadBytes.AsSpan(0), counter);
        BinaryPrimitives.WriteUInt16LittleEndian(payloadBytes.AsSpan(4), smoothing);

        var offset = 6;
        foreach (var state in states)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(payloadBytes.AsSpan(offset), state.Address);

            var packed = (ushort)((state.Brightness << 5) | 11);
            BinaryPrimitives.WriteUInt16LittleEndian(payloadBytes.AsSpan(offset + 2), packed);

            var x12 = (ushort)Math.Clamp(Math.Round(state.X / CieXMax * 4), 0, 4095);
            var y12 = (ushort)Math.Clamp(Math.Round(state.Y / CieYMax * 4), 0, 4095);

            payloadBytes[offset + 4] = (byte)(x12 & 0xFF);
            payloadBytes[offset + 5] = (byte)(((x12 >> 8) & 0x0F) | ((y12 & 0x0F) << 4));
            payloadBytes[offset + 6] = (byte)((y12 >> 4) & 0xFF);

            offset += 7;
        }

        var dataArrayStr = "[" + string.Join(",", payloadBytes) + "]";

        var jsonPayload =
            $"{{\"zclcommand\":{{\"cluster\":64513,\"command\":1,\"payload\":{{\"data\":{dataArrayStr}}},\"frametype\":1,\"options\":{{\"manufacturerCode\":4107,\"disableDefaultResponse\":true}}}}}}";

        var topic = $"{Config.Z2MBaseTopic}/{proxy.FriendlyName}/set";
        var message = new MqttApplicationMessageBuilder().WithTopic(topic).WithPayload(jsonPayload).Build();
        await client.PublishAsync(message, CancellationToken.None);
    }

    public static ushort CalculateSmoothing(int fps)
    {
        const double smoothingMaxMicros = 2_560_000.0;
        var frameIntervalMicros = 1_000_000.0 / fps;
        var smoothingRatio = frameIntervalMicros / smoothingMaxMicros;
        return (ushort)Math.Min(0xFFFF, Math.Round(smoothingRatio * 0xFFFF));
    }

    public static async Task StartAllLights(IMqttClient client, List<Light> lights, MqttClientFactory factory)
    {
        Console.WriteLine("Sending START command to all lights...");
        var startPayloadBytes = new byte[4];
        startPayloadBytes[0] = 0;
        startPayloadBytes[1] = 1;

        var z2mPayload = new Dictionary<string, object>
        {
            ["zclcommand"] = new Dictionary<string, object>
            {
                ["cluster"] = 0xFC01,
                ["command"] = 0,
                ["payload"] = new Dictionary<string, object>
                {
                    ["data"] = startPayloadBytes.Select(b => (int)b).ToArray()
                },
                ["frametype"] = 1,
                ["options"] = new Dictionary<string, object>
                {
                    ["manufacturerCode"] = ManufacturerId,
                    ["disableDefaultResponse"] = true
                }
            }
        };
        var jsonPayload = JsonSerializer.Serialize(z2mPayload);

        var tasks = lights.Select(light =>
        {
            var topic = $"{Config.Z2MBaseTopic}/{light.FriendlyName}/set";
            var message = factory.CreateApplicationMessageBuilder().WithTopic(topic).WithPayload(jsonPayload).Build();
            return client.PublishAsync(message, CancellationToken.None);
        });
        await Task.WhenAll(tasks);
        Console.WriteLine("Start command sent to all lights.");
    }

    public static async Task StopAllLights(IMqttClient client, List<Light> lights, MqttClientFactory factory,
        uint counter = 0)
    {
        Console.WriteLine($"Sending STOP command (counter: {counter}) to all lights...");
        var stopPayloadBytes = new byte[6];
        stopPayloadBytes[0] = 0;
        stopPayloadBytes[1] = 1;
        BitConverter.GetBytes(counter).CopyTo(stopPayloadBytes, 2);

        var z2mPayload = new Dictionary<string, object>
        {
            ["zclcommand"] = new Dictionary<string, object>
            {
                ["cluster"] = 0xFC01,
                ["command"] = 3,
                ["payload"] = new Dictionary<string, object>
                {
                    ["data"] = stopPayloadBytes.Select(b => (int)b).ToArray()
                },
                ["frametype"] = 1,
                ["options"] = new Dictionary<string, object>
                {
                    ["manufacturerCode"] = ManufacturerId,
                    ["disableDefaultResponse"] = true
                }
            }
        };
        var jsonPayload = JsonSerializer.Serialize(z2mPayload);

        var tasks = lights.Select(light =>
        {
            var topic = $"{Config.Z2MBaseTopic}/{light.FriendlyName}/set";
            var message = factory.CreateApplicationMessageBuilder().WithTopic(topic).WithPayload(jsonPayload).Build();
            return client.PublishAsync(message, CancellationToken.None);
        });
        await Task.WhenAll(tasks);
        Console.WriteLine("All lights have been reset.");
    }
}