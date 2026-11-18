using Unity.MLAgents.Sensors;
using UnityEngine;

namespace LayoutRecommender.RL
{
    /// <summary>
    /// 배치 격자를 다채널 이미지(높이 GridH × 너비 GridW × 채널)로 관측한다. 학습에서는 CNN(match3 인코더)이 읽는다.
    /// 채널: 0 기둥 · 1 입고 도크 · 2 출고 도크 · 3 점유(아무 구역) · 4~8 구역 종류별 점유 · 9 현재 구역 유효 위치(회전 0) · 10 유효 위치(회전 90°)
    /// </summary>
    public class LayoutGridSensor : ISensor
    {
        public const int Channels = 4 + LayoutProblem.ZoneTypeCount + 2;

        readonly LayoutPlacementAgent _agent;
        readonly string _name;
        readonly ObservationSpec _spec;

        public LayoutGridSensor(LayoutPlacementAgent agent, string name)
        {
            _agent = agent;
            _name = name;
            _spec = ObservationSpec.Visual(LayoutProblem.GridH, LayoutProblem.GridW, Channels);
        }

        public ObservationSpec GetObservationSpec() => _spec;

        public int Write(ObservationWriter writer)
        {
            var env = _agent != null ? _agent.Env : null;
            int W = LayoutProblem.GridW, H = LayoutProblem.GridH;

            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    for (int c = 0; c < Channels; c++)
                        writer[y, x, c] = 0f;

            if (env?.Problem == null) return W * H * Channels;
            var p = env.Problem;

            foreach (var d in p.InboundDocks) writer[d.y, d.x, 1] = 1f;
            foreach (var d in p.OutboundDocks) writer[d.y, d.x, 2] = 1f;

            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    if (p.Obstacle[x, y]) writer[y, x, 0] = 1f;
                    int k = env.Cell[x, y];
                    if (k >= 0)
                    {
                        writer[y, x, 3] = 1f;
                        writer[y, x, 4 + (int)p.Zones[k].Zone] = 1f;
                    }
                    if (!env.Done)
                    {
                        if (env.IsValid(LayoutEnv.Encode(x, y, false))) writer[y, x, 9] = 1f;
                        if (env.IsValid(LayoutEnv.Encode(x, y, true))) writer[y, x, 10] = 1f;
                    }
                }
            return W * H * Channels;
        }

        public byte[] GetCompressedObservation() => null;
        public CompressionSpec GetCompressionSpec() => CompressionSpec.Default();
        public void Update() { }
        public void Reset() { }
        public string GetName() => _name;
    }

    /// <summary>에이전트와 같은 오브젝트에 붙이면 ML-Agents가 위 센서를 자동으로 등록한다.</summary>
    public class LayoutGridSensorComponent : SensorComponent
    {
        public string SensorName = "LayoutGrid";

        public override ISensor[] CreateSensors()
        {
            var agent = GetComponent<LayoutPlacementAgent>();
            return new ISensor[] { new LayoutGridSensor(agent, SensorName) };
        }
    }
}
