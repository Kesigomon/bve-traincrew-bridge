using System.Reflection;
using System.Text;
using bve_traincrew_bridge;
using TrainCrew;
using TrainState = TrainCrew.TrainState;

internal class Program
{
    private TimeSpan PreviousTime {  get; set; }
    private bool PreviousDoorClose { get; set; } = true;
    private int PreviousHandPnotch = 0;
    private int PreviousHandBnotch = 0;
    private int PreviousTascPNotch = -1;
    private int PreviousTascBNotch = -1;
    private int PreviousReverser = 0;
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
                }
                
                // プレイ中であれば諸々の処理を行う
                if (
                    TrainCrewInput.gameState.gameScreen == GameScreen.MainGame
                )
                {
                    if (firstGameLoop)
                    {
                        PreviousHandPnotch = state.Pnotch;
                        PreviousHandBnotch = state.Bnotch;
                        PreviousReverser = 2;
                        PreviousTascPNotch = -1;
                        PreviousTascBNotch = -1;
                    }
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
                    // 手動操作をBVEプラグイン側に伝える(1フレーム前のプラグインの操作と比較して、変化があればそれは手動操作と認識できる)
                    if(state.Bnotch != PreviousTascBNotch && state.Bnotch != PreviousHandBnotch)
                    {
                        Console.WriteLine("Changed to B" + (PreviousHandBnotch - 1) + "->B" + (state.Bnotch-1));
                        setBrake(state.Bnotch);
                        setPower(0);
                        PreviousHandBnotch = state.Bnotch;
                    }
                    else if(state.Pnotch != PreviousTascPNotch && state.Pnotch != PreviousHandPnotch)
                    {
                        Console.WriteLine("Changed to P" + PreviousHandPnotch + "->P" + state.Pnotch);
                        setPower(state.Pnotch);
                        setBrake(0);
                        PreviousHandPnotch = state.Pnotch;
                    }
                    if(state.Reverser != PreviousReverser)
                    {
                        Console.WriteLine("Changed to R" + PreviousReverser + "->R" + state.Reverser);
                        setReverse(state.Reverser);
                        PreviousReverser = state.Reverser;
                    }
                    // フレーム処理
                    var handle = elapse(state);
                    // 結果をTrainCrew側に反映
                    if (PreviousTascBNotch != handle.Brake)
                    {
                        TrainCrewInput.SetATO_Notch(-handle.Brake);
                        PreviousTascBNotch = handle.Brake;
                    }
                    else if (handle.Brake == 0 && PreviousTascPNotch != handle.Power)
                    {
                        TrainCrewInput.SetATO_Notch(handle.Power);
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
    }

    private void loadDiagram()
    {
        AtsPlugin.Initialize(1);
        Handler.Reset();
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
        vehicleState.BcPressure = trainState.CarStates[0].BC_Press;
        vehicleState.MrPressure = trainState.MR_Press;
        // Todo: TrainCrew側で実装されたら正しい値に変える
        vehicleState.ErPressure = 0;
        vehicleState.BpPressure = 0;
        vehicleState.SapPressure = 0;
        vehicleState.Current = trainState.CarStates[0].Ampare;
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