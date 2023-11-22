using System.Runtime.InteropServices;
using TrainCrew;



internal class Program
{
    private TimeSpan PreviousTime {  get; set; }
    private bool PreviousDoorClose { get; set; } = true;
    private int PreviousPnotch = 0;
    private int PreviousBnotch = 0;
    private BeaconHandler Handler;
    private GameScreen? PreviousGameScreen{ get; set; }

    Program()
    {
        Handler = new BeaconHandler();
    }

    private static void Main(string[] args)
    {
        var program = new Program();
        var task = program.main(args);
        task.Wait();
    }

    private async Task main(string[] args)
    {
        TrainCrewInput.Init();
        try
        {
            loadPlugin();
            var firstLoop = true;
            var firstGameLoop = true;
            PreviousGameScreen = TrainCrewInput.gameState.gameScreen;
            while (true)
            {
                TrainCrewInput.RequestStaData();
                var timer = Task.Delay(16);
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
                    firstLoop = false;
                    firstGameLoop = true;
                }
                // ゲームポーズ中からローディングに入った場合、最初から読み込みを選んだので、路線のみを再読み込み
                else if (
                    PreviousGameScreen == GameScreen.MainGame_Pause
                    && isFirstLoadingFrame
                )
                {
                    loadDiagram();
                }
                
                // プレイ中であれば諸々の処理を行う
                if (TrainCrewInput.gameState.gameScreen == GameScreen.MainGame)
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
                    // 手動操作をBVEプラグイン側に伝える
                    if(state.Pnotch != PreviousPnotch)
                    {
                        setPower(state.Pnotch);
                        PreviousPnotch = state.Pnotch;
                    }
                    if(state.Bnotch != PreviousBnotch)
                    {
                        setBrake(state.Bnotch);
                        PreviousBnotch = state.Bnotch;
                    }
                    // フレーム処理
                    var handle = elapse(state);
                    // 結果をTrainCrew側に反映
                    if (state.Pnotch != handle.Power)
                    {
                        TrainCrewInput.SetNotch(handle.Power);   
                    }
                    else if (state.Bnotch != handle.Brake)
                    {
                        TrainCrewInput.SetNotch(-handle.Brake);
                    }
                    PreviousPnotch = handle.Power;
                    PreviousBnotch = handle.Brake;

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