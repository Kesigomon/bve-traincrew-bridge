using System.Diagnostics;
using TrainCrew;

class SignalData
{
    public int SpeedLimit { get; set; }
    public int index { get; set; }
}

class CheckedSignalValue
{
    public required string Phase { get; set; }
    // 最初に信号検知したときのキロ程から算出した信号の絶対位置
    public required float Position { get; set; }
}

internal class BeaconHandler
{
    private Dictionary<string, SignalData> SIGNALS = new()
    {
        // 信号の設定
        // 停止(0km/h)
        { "R", new SignalData { SpeedLimit = 0, index = 0 } },
        // 警戒(25km/h)
        { "YY", new SignalData { SpeedLimit = 25, index = 1 } },
        // 注意(55km/h)
        { "Y", new SignalData { SpeedLimit = 55, index = 2 } },
        // 減速(80km/h)
        { "YG", new SignalData { SpeedLimit = 80, index = 3 } },
        // 進行(110km/h)
        { "G", new SignalData { SpeedLimit = 110, index = 4 } },
    };
    private float _previousNextStaDistance;
    private float _previousSpeedLimit;
    private float _previousNextSpeedLimit;
    private int _lastChanedNextSpeedLimit;
    private float _previousGradient;
    private int _previousNowStaIndex;
    private bool _first = true;
    // 新野崎江ノ原間の速度制限処理済みフラグ(デフォルトはTrueだが、上りの場合は自動でfalseになる)
    private bool _isHandledSpeedLimitEnohara = true;
    
    private Dictionary<string, CheckedSignalValue> checkedSignal = new ();

    private static bool IsUp(string diaName)
    {
        for(int i = 0; i < diaName.Length; i++)
        {
            char c = diaName[diaName.Length - i - 1];
            try
            {
                int n = int.Parse(c.ToString());
                return n % 2 == 0;
            }
            catch (FormatException)
            {
            }
        }
        // Unreachable
        throw new UnreachableException();
    }
    
    public void Reset()
    {
        _previousNextStaDistance = 0;
        _previousSpeedLimit = -1;
        _previousNextSpeedLimit = -1;
        _previousGradient = 0;
        _lastChanedNextSpeedLimit = 0;
        _previousNowStaIndex = 0;
        _first = true;
        checkedSignal.Clear();
    }
    public void HandleBeacon(TrainState state, List<SignalInfo> signalInfos)
    {
        if (state == null) return;
        if (_first)
        {
            // 停止許容誤差設定(デフォルトは200cm)
            AtsPlugin.SetBeaconData(new AtsPlugin.ATS_BEACONDATA
            {
                Type = 1031,
                Optional = 200,
            });
            // 信号の設定
            foreach (var signal in SIGNALS.Values)
            {
                AtsPlugin.SetBeaconData(new AtsPlugin.ATS_BEACONDATA
                {
                    Type = 1011,
                    Optional = signal.index * 1000 + signal.SpeedLimit,
                });
            }
            //営業最大速度(110km/h)
            AtsPlugin.SetBeaconData(new AtsPlugin.ATS_BEACONDATA
            {
                Type = 1007,
                Optional = 110,
            });
            // Todo: 目標減速度を設定可能にする
            // 上りの場合に、新野崎江ノ原間の制限速度の処理を有効にする
            _isHandledSpeedLimitEnohara = !IsUp(state.diaName);
            _first = false;
        }
        HandleGeneralBeacon(state);
        // TASCに必要なビーコン
        HandleTascBeacon(state);
        // ATOに必要なビーコン(制限速度etc...)
        HandleAtoBeacon(state, signalInfos);
    }

    private void HandleGeneralBeacon(TrainState state)
    {
        // 勾配
        if (state.gradient == _previousGradient)
            return;
        var beacon = new AtsPlugin.ATS_BEACONDATA();
        beacon.Type = 1008;
        beacon.Optional = (int)state.gradient;
        AtsPlugin.SetBeaconData(beacon);
        _previousGradient = state.gradient;
    }

    private void HandleTascBeacon(TrainState state)
    {
        if (state.nextStopType != "停車" && state.nextStopType != "運転停車")
        {
            return;
        }
        // Todo: 地上子を置く位置を指定可能にする
        foreach (var distance in new float[]{ 900, 700, 500, 300, 10 })
        {
            if (state.nextStaDistance >= distance ||  _previousNextStaDistance < distance)
            {
                continue;
            }
            var beacon = new AtsPlugin.ATS_BEACONDATA();
            // SignalとDistanceはここでは使わないので未設定でOK
            beacon.Type = 1030;
            beacon.Optional = (int)(state.nextStaDistance * 1000);
            AtsPlugin.SetBeaconData(beacon);
        }
        _previousNextStaDistance = state.nextStaDistance;
    }

    private void HandleAtoBeacon(TrainState state, List<SignalInfo> signalInfos)
    {
        HandleScheduleBeacon(state);
        // Todo: 各処理の分離
        var signals = new List<SignalInfo>();
        // 信号を設置地点にわけ、見るべき信号機を入れる(各現示のMaxを取る)
        foreach (SignalInfo signalInfo in signalInfos)
        {
            int index = signals.FindIndex(info => double.Abs(info.distance - signalInfo.distance) < 1);
            if (index == -1)
            {
                signals.Add(signalInfo);
            }
            else if (SIGNALS[signalInfo.phase].SpeedLimit > SIGNALS[signals[index].phase].SpeedLimit)
            {
                signals[index] = signalInfo;
            }
        }
        signals.Sort((signal1, signal2) => signal1.distance.CompareTo(signal2.distance));
        // 信号を設置地点(≒閉塞)ごとにプラグインに送る
        foreach (var signalInfo in signals)
        {
            // 確認済み信号は無視する
            if (checkedSignal.TryGetValue(signalInfo.name, out CheckedSignalValue? oldSignal) && oldSignal.Phase == signalInfo.phase)
            {
                break;
            }
            float position;
            if(oldSignal == null)
            {
                position = state.TotalLength + signalInfo.distance;
            }
            else
            {
                position = oldSignal.Position;
            }
            // 確認済みフラグを立てる
            checkedSignal[signalInfo.name] = new CheckedSignalValue
            {
                Phase = signalInfo.phase,
                Position = position
            };
            int signalIndex = SIGNALS[signalInfo.phase].index;
            // キロ程と絶対位置から信号までの距離を算出する
            // 閉塞はキロ程との差で同一かどうかを判定してるので、最初に受信した位置で絶対位置を固定しておいて
            // そこからキロ程との差で渡してあげたほうがズレなく閉塞を検知させてやれる
            float distance = position - state.TotalLength;
            AtsPlugin.SetBeaconData(new AtsPlugin.ATS_BEACONDATA
            {
                Type = 1012,
                Signal = signalIndex,
                Distance = distance,
                Optional = 0,
            });
            signalInfo.beacons.ForEach(beacon =>
            {
                if (beacon.type != "SigIfStop")
                    return;
                // 制限かかるまでが少し遅い気がする？ので150m手前に設定
                int data = 1000 * (int)Math.Max(0, beacon.distance - 150) + (int)beacon.speed;
                AtsPlugin.SetBeaconData(new AtsPlugin.ATS_BEACONDATA
                {
                    Type = 1016,
                    Signal = signalIndex,
                    Distance = distance,
                    Optional = data,
                });
            });
            break;
        }
        // 制限速度
        // 現在の制限速度
        if (_previousSpeedLimit != state.speedLimit)
        {
            Console.WriteLine("Send Speed Limit: " + state.speedLimit);
            AtsPlugin.SetBeaconData(new AtsPlugin.ATS_BEACONDATA
            {
                Type = 1006,
                Optional = (int)state.speedLimit,
            });
            _previousSpeedLimit = state.speedLimit;
        }
        // 次の制限速度
        // 新野崎-江ノ原間の制限速度はAPIからの取得が遅すぎて減速が間に合わないので、定数的に処理する
        if (!_isHandledSpeedLimitEnohara && state.nextStaName == "江ノ原" && state.nextStaDistance < 1500)
        {
            int distance = (int)(state.nextStaDistance - 923);
            int data = 1000 * distance + 65;
            AtsPlugin.SetBeaconData(new AtsPlugin.ATS_BEACONDATA
            {
                Type = 1006,
                Optional = data,
            });
            _isHandledSpeedLimitEnohara = true;
        }
        // 速度制限が変わっていなければ、保留中の速度制限を処理する
        if (_previousNextSpeedLimit == state.nextSpeedLimit)
        {
            _lastChanedNextSpeedLimit += 1;
            if (_lastChanedNextSpeedLimit != 3 || state.nextSpeedLimit <= 0)
                return;
            int data = 1000 * (int)state.nextSpeedLimitDistance + (int)state.nextSpeedLimit;
            Console.WriteLine("Send Next Speed Limit: " + data);
            AtsPlugin.SetBeaconData(new AtsPlugin.ATS_BEACONDATA
            {
                Type = 1006,
                Optional = data,
            });
            return;
        }
        
        // 速度制限が設定されていない or 停止の場合は処理しない(0を送るとプラグイン側は速度制限を切ってしまう)
        if (state.nextSpeedLimit <= 0)
        {
            _previousNextSpeedLimit = state.nextSpeedLimit;
            return;
        }
        // 信号による制限速度でないかの判定
        foreach (var signal in signals)
        {
            bool notPassed = true;
            foreach (var beacon in signal.beacons)
            {
                if(beacon.type is not ("SigIfStop" or "Signal"))
                {
                    continue;
                }
                notPassed = false;
                if (double.Abs(beacon.distance - state.nextSpeedLimitDistance) >= 1)
                    continue;
                // 信号による制限速度の場合、処理をココでやめる
                _previousNextSpeedLimit = state.nextSpeedLimit;
                return;
            }

            // ビーコン情報がまだ来ていない場合、来てから処理したいので処理をココでやめる
            if (notPassed)
            {
                return;
            }
        }
        // 信号による制限速度でない場合、一旦保留する(一瞬だけ謎の速度制限が表示されるバグが残っているので)
        _previousNextSpeedLimit = state.nextSpeedLimit;
        _lastChanedNextSpeedLimit = 0;
    }

    private void HandleScheduleBeacon(TrainState state)
    {
        if(_previousNowStaIndex == state.nowStaIndex)
        {
            return;
        }
        // 次の採択駅を探す
        for(int i = state.nowStaIndex; i < state.stationList.Count; i++)
        {
            Station station = state.stationList[i];
            if (double.Abs(station.TotalLength - state.TotalLength - state.nextUIDistance) >= 100)
            {
                continue;
            }
            // 次の採択駅が見つかったら、その駅のダイヤ情報をおくる
            AtsPlugin.SetBeaconData(new AtsPlugin.ATS_BEACONDATA
            {
                Type = 1028,
                Optional = (int)station.ArvTime.TotalSeconds,
            });
            int data = 1000 * (int)state.nextUIDistance;
            if (station.stopType == StopType.Passing)
            {
                data += 999;
            }
            AtsPlugin.SetBeaconData(new AtsPlugin.ATS_BEACONDATA
            {
                Type = 1029,
                Optional = data,
            });
            break;
        }
        _previousNowStaIndex = state.nowStaIndex;
    }
}
