using Tanuden.Common;
using Tanuden.TIMS.API;

namespace bve_traincrew_bridge;

public class TanudenIntegration
{
    private static readonly string TanudenPluginUid = "kesigomon.bve_traincrew_bridge";

    internal static void RegisterApi()
    {
        Console.WriteLine("タヌ電のAPIを待機中...");
        TanudenTIMSAPI.WaitForApi();
        Console.WriteLine("タヌ電のAPIに接続オーライ！");

        TanudenTIMSAPI.Init(new PluginMeta
        {
            Uid = TanudenPluginUid,
            Name = "BVE TrainCrew Bridge",
            Version = Program.Version!,
            Author = "Kesigomon"
        });
    }

    internal static void SendTascPanel(int[] panel)
    {
        TanudenTIMSAPI.SendData(new PluginState
        {
            Uid = TanudenPluginUid,
            Data = new
            {
                trainState = new
                {
                    lamps = new
                    {
                        tasc = new
                        {
                            power = panel[Config.TASCConfig.tascenabled] != 0,
                            monitor = panel[Config.TASCConfig.tascmonitor] != 0,
                            brake = panel[Config.TASCConfig.tascbrake],
                            position = panel[Config.TASCConfig.tascposition],
                            inching = panel[Config.TASCConfig.inching] != 0
                        }
                    }
                }
            }
        });
    }
}