using TrainCrew;

internal class BeaconHandler
{
    private float _previousNextStaDistance;
    private float _previousSpeedLimit;
    private float _previousNextSpeedLimit;
    private float _previousGradient;
    private bool _first = true;
    public void Reset()
    {
        _previousNextStaDistance = 0;
        _previousSpeedLimit = 0;
        _previousNextSpeedLimit = -1;
        _previousGradient = 0;
        _first = true;
    }
    public void HandleBeacon(TrainState state)
    {
        if (state == null) return;
        if (_first)
        {
            // 停止許容誤差設定(デフォルトは200cm)
            AtsPlugin.SetBeaconData(new AtsPlugin.ATS_BEACONDATA
            {
                Type = 1031,
                Optional = 200
            });
        }
        HandleGeneralBeacon(state);
        // TASCに必要なビーコン
        HandleTascBeacon(state);
        // ATOに必要なビーコン(制限速度etc...)
        HandleAtoBeacon(state);
    }

    private void HandleGeneralBeacon(TrainState state)
    {
        // 勾配
        if(state.gradient != _previousGradient)
        {
            var beacon = new AtsPlugin.ATS_BEACONDATA();
            beacon.Type = 1008;
            beacon.Optional = (int) state.gradient;
            AtsPlugin.SetBeaconData(beacon);
            _previousGradient = state.gradient;
        }
        
        // 制限速度
        if(state.speedLimit != _previousSpeedLimit || true)
        {
            var beacon = new AtsPlugin.ATS_BEACONDATA();
            beacon.Type = 1006;
            beacon.Optional = (int) state.speedLimit;
            AtsPlugin.SetBeaconData(beacon);
            _previousSpeedLimit = state.speedLimit;
        }
        if(state.nextSpeedLimit != _previousNextSpeedLimit || true)
        {
            var beacon = new AtsPlugin.ATS_BEACONDATA();
            beacon.Type = 1007;
            // -1の場合は制限速度解除
            if (state.nextSpeedLimit == -1)
            {
                beacon.Optional = 0;
            }
            else
            {
                beacon.Optional = 1000 * (int)state.nextSpeedLimitDistance + (int)state.nextSpeedLimit;
            }
            AtsPlugin.SetBeaconData(beacon);
            _previousNextSpeedLimit = state.nextSpeedLimit;
        }
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

    private void HandleAtoBeacon(TrainState state)
    {
    }
}