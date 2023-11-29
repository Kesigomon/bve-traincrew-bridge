using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using bve_traincrew_bridge;
using Tanuden.Common;
using Tanuden.TIMS.API;
using TrainCrew;
using TrainState = TrainCrew.TrainState;

internal class Program
{
    private TimeSpan PreviousTime {  get; set; }
    private bool PreviousDoorClose { get; set; } = true;
    private int PreviousPnotch = 0;
    private int PreviousBnotch = 0;
    private int PreviousTascPNotch = -1;
    private int PreviousTascBNotch = -1;
    private BeaconHandler Handler;
    private GameScreen? PreviousGameScreen{ get; set; }
    
    // Get version of this assembly
    internal static string? Version => Assembly.GetExecutingAssembly().GetName().Version?.ToString();

    Program()
    {
        Handler = new BeaconHandler();
    }

    private static void Main(string[] args)
    {
        // Load config
        Config.LoadConfig();
        
        var program = new Program();
        var task = program.main(args);
        task.Wait();
    }

    private async Task main(string[] args)
    {
        // Enable Kana/Kanji console output
        Console.OutputEncoding = Encoding.GetEncoding("UTF-8");
        
        TrainCrewInput.Init();

        if (Config.ApiEnable)
        {
            TanudenIntegration.RegisterApi();
        }
        
        try
        {
            loadPlugin();
            var firstLoop = true;
            var firstGameLoop = true;
            PreviousGameScreen = TrainCrewInput.gameState.gameScreen;
            float previousSpeed = 0;
            while (true)
            {
                TrainCrewInput.RequestStaData();
                var timer = Task.Delay(17);
                TrainState state = TrainCrewInput.GetTrainState();
                GameState gameState = TrainCrewInput.gameState;
                // ゲームロードしてから最初のフレームであるか
                bool isFirstLoadingFrame = PreviousGameScreen != GameScreen.MainGame_Loading && gameState.gameScreen == GameScreen.MainGame_Loading;
                // ゲームプレイ中以外からローディングに入った場合は乗務をえらんだので、全て読み込み
                if (
                    (PreviousGameScreen != GameScreen.MainGame 
                    && isFirstLoadingFrame
                    ) || firstLoop
                )
                {
                    loadTrain(state);
                }
                // ゲームポーズ中からローディングに入った場合、最初から読み込みを選んだので、路線のみを再読み込み
                else if (
                    PreviousGameScreen == GameScreen.MainGame_Pause
                    && isFirstLoadingFrame
                )
                {
                    loadDiagram();
                }
                // ゲームをロードしたときの処理
                if (isFirstLoadingFrame　|| firstLoop)
                {
                    firstLoop = false;
                    firstGameLoop = true;
                    previousSpeed = 0;
                    PreviousPnotch = 0;
                    PreviousBnotch = 0;
                    PreviousTascPNotch = -1;
                    PreviousTascBNotch = -1;
                }
                
                // プレイ中であれば諸々の処理を行う
                if (
                    TrainCrewInput.gameState.gameScreen == GameScreen.MainGame 
                    // 速度が変化していない場合、フレーム処理が行われていないと推測できるのでフレーム処理をスキップ
                    && (state.Speed == 0 || state.Speed != previousSpeed)
                )
                {
                    // ドアの開閉処理
                    if (state.AllClose != PreviousDoorClose || firstGameLoop)
                    {
                        if (state.AllClose)
                        {
                            doorClose();
                        }
                        else
                        {
                            doorOpen();
                        }
                    }
                    var isControllerChanged = false;
                    
                    // TASC/ATOの操作適用を確認する
                    if (PreviousTascBNotch >= 0)
                    {
                        if (PreviousTascBNotch == state.Bnotch)
                        {
                            PreviousTascBNotch = -1;
                        }
                    }
                    else if (PreviousTascPNotch >= 0)
                    {
                        if (PreviousTascPNotch == state.Pnotch)
                        {
                            PreviousTascPNotch = -1;
                        }
                    }
                    // TASC/ATOノッチの操作の影響がなければ、手動操作をBVEプラグイン側に伝える
                    else
                    {
                        if(state.Bnotch != PreviousBnotch)
                        {
                            Console.WriteLine("Changed to B" + (PreviousBnotch - 1) + "->B" + (state.Bnotch-1));
                            setBrake(state.Bnotch);
                            isControllerChanged = true;
                        }
                        else if(state.Pnotch != PreviousPnotch)
                        {
                            Console.WriteLine("Changed to P" + PreviousPnotch + "->P" + state.Pnotch);
                            setPower(state.Pnotch);
                            isControllerChanged = true;
                        }
                    }
                    // フレーム処理
                    var handle = elapse(state);
                    PreviousPnotch = handle.Brake == 0 ? handle.Power : 0;
                    PreviousBnotch = handle.Brake;
                    // 結果をTrainCrew側に反映
                    if (isControllerChanged)
                    {
                        // ハンドル操作があった場合は、TASC/ATOの操作は適用しない
                        PreviousBnotch = state.Bnotch;
                        PreviousPnotch = state.Pnotch;
                    }
                    else if (state.Bnotch != handle.Brake)
                    {
                        TrainCrewInput.SetNotch(-handle.Brake);
                        PreviousTascBNotch = handle.Brake;
                    }
                    else if (handle.Brake == 0 && state.Pnotch != handle.Power)
                    {
                        TrainCrewInput.SetNotch(handle.Power);
                        PreviousTascPNotch = handle.Power;
                    }
                    
                    // TrainCrewInput.SetReverser(handle.Reverser);
                    // ビーコン処理
                    handleBeacon(state);
                    firstGameLoop = false;
                }
                PreviousGameScreen = gameState.gameScreen;
                PreviousDoorClose = state.AllClose;
                PreviousTime = state.NowTime;
                previousSpeed = state.Speed;
                await timer;
            }
        }
        finally
        {
            TrainCrewInput.Dispose();
            disposePlugin();
        }
        
    }

    private void handleBeacon(TrainState state)
    {
        // 運転状況に合わせてビーコンを通過した旨をBVEプラグインに送る
        // ここは、プラグインや状況に応じてカスタムが必要になる
        Handler.HandleBeacon(state);
    }


    private void loadPlugin()
    {
        AtsPlugin.Load();
    }

    private void disposePlugin()
    {
        AtsPlugin.Dispose();
    }

    private void loadTrain(TrainState trainState)
    {
        var spec = new AtsPlugin.ATS_VEHICLESPEC();
        spec.Cars = trainState.CarStates.Count;
        spec.BrakeNotches = 8;
        spec.PowerNotches = 5;
        spec.B67Notch = 6;
        spec.AtsNotch = 8;
        AtsPlugin.SetVehicleSpec(spec);
        loadDiagram();
        setReverse(1);
    }

    private void loadDiagram()
    {
        Handler.Reset();
        AtsPlugin.Initialize(1);
    }
    
    private AtsPlugin.ATS_HANDLES elapse(TrainState trainState)
    {
        var vehicleState = new AtsPlugin.ATS_VEHICLESTATE();
        // 現在位置の計算
        // 現在位置は次の駅との距離差分で計算する
        if (trainState.stationList.Count > trainState.nowStaIndex)
        {
            var nextStation = trainState.stationList[trainState.nowStaIndex];
            vehicleState.Location = nextStation.TotalLength - trainState.nextStaDistance;
        }
        else
        {
            vehicleState.Location = 0;
        }
        vehicleState.Speed = trainState.Speed;
        vehicleState.Time = (int)trainState.NowTime.TotalMilliseconds;
        // Todo: carState使ってブレーキシリンダ圧力を設定
        vehicleState.BcPressure = 0;
        vehicleState.MrPressure = trainState.MR_Press;
        // Todo: TrainCrew側で実装されたら正しい値に変える
        vehicleState.ErPressure = 0;
        vehicleState.BpPressure = 0;
        vehicleState.SapPressure = 0;
        // Todo: carState使って電流を設定
        vehicleState.Current = 0;
        var panel = new int[256];
        var sound = new int[256];
        var result = AtsPlugin.Elapse(vehicleState, panel, sound);

        // もしREST APIを有効にしていたら、TASCデータを更新する
        if (Config.ApiEnable)
        {
            TanudenIntegration.SendTascPanel(panel);
        }
        
        return result;
    }

    private void doorOpen()
    {
        AtsPlugin.DoorOpen();
    }

    private void doorClose()
    {
        AtsPlugin.DoorClose();
    }

    private void setPower(int notch)
    {
        AtsPlugin.SetPower(notch);
    }

    private void setBrake(int notch)
    {
        AtsPlugin.SetBrake(notch);
    }

    private void setReverse(int reverser)
    {
        AtsPlugin.SetReverser(reverser);

    }

}