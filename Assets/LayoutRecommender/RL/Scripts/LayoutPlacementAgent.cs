using LayoutRecommender.Data;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;
using Unity.MLAgents.Sensors;
using UnityEngine;

namespace LayoutRecommender.RL
{
    /// <summary>
    /// 물류센터 구역을 한 번에 하나씩 놓는 ML-Agents 에이전트.
    ///
    /// 에피소드 = 레이아웃 1안. 스텝 = 구역 1개 배치.
    /// - 관측: 격자 이미지(LayoutGridSensor) + 현재 구역 정보 벡터(16)
    /// - 행동: 이산 1개 브랜치, 크기 GridW × GridH × 2 (위치 × 회전). 겹침·통로 차단 위치는 마스킹
    /// - 보상: 스텝마다 −CostWeight × (물동량×거리 증가분 / 정규화값), 완성 시 +CompletionBonus, 실패 시 FailPenalty
    ///   → 완성된 안의 총보상 = CompletionBonus − CostWeight × (최종 비용 / 정규화값)
    /// </summary>
    [RequireComponent(typeof(LayoutView))]
    [RequireComponent(typeof(LayoutGridSensorComponent))]
    public class LayoutPlacementAgent : Agent
    {
        public const string BehaviorNameConst = "LayoutPlacer";
        public const int VectorObsSize = LayoutProblem.ZoneTypeCount + 2 + LayoutProblem.ZoneTypeCount + 2 + 2;

        public enum ProblemSource
        {
            RandomTraining, // 매 에피소드 무작위 문제 (학습용)
            ProjectInput    // 앱에서 저장한 마지막 입력(last_input.json), 없으면 샘플 입력
        }

        [Header("문제")]
        public ProblemSource Source = ProblemSource.RandomTraining;
        [Tooltip("무작위 문제의 구역 수 (2~5). YAML의 environment_parameters.zone_count가 있으면 그 값을 쓴다.")]
        [Range(2, LayoutProblem.MaxZones)] public int ZoneCount = 3;
        public int Seed = 0;

        [Header("보상")]
        public float CostWeight = 4f;
        public float CompletionBonus = 1f;
        public float FailPenalty = -1f;
        [Tooltip("N 에피소드마다 같은 문제를 탐욕 배치로도 풀어 비교값을 TensorBoard에 기록 (0이면 끔)")]
        public int CompareWithGreedyEvery = 20;

        [Header("화면")]
        [Tooltip("0이면 매 스텝 결정 (학습용). 0보다 크면 이 간격(초)마다 한 구역씩 놓아 보여준다.")]
        public float StepInterval = 0f;
        [Tooltip("시연 모드에서 완성된 안을 보여주는 시간(초)")]
        public float EndPause = 2f;
        [Tooltip("학습 중에도 매 스텝 그리기 (느려짐)")]
        public bool RenderEveryStep = false;

        public LayoutEnv Env { get; } = new LayoutEnv();
        public float LastCost { get; private set; }
        public float LastGreedyCost { get; private set; } = -1f;
        public int EpisodesDone { get; private set; }

        LayoutView _view;
        System.Random _rng;
        float _partialCost;
        float _timer;
        float _pauseUntil;
        readonly LayoutEnv _greedyEnv = new LayoutEnv();

        /// <summary>Behavior Parameters를 코드에 맞게 설정한다. 씬 생성 메뉴와 실행 시 모두 호출된다.</summary>
        public static void ConfigureBehavior(BehaviorParameters bp)
        {
            bp.BehaviorName = BehaviorNameConst;
            bp.BrainParameters.VectorObservationSize = VectorObsSize;
            bp.BrainParameters.NumStackedVectorObservations = 1;
            bp.BrainParameters.ActionSpec = ActionSpec.MakeDiscrete(LayoutEnv.ActionCount);
        }

        protected override void OnEnable()
        {
            // Agent 초기화(base.OnEnable) 전에 관측/행동 크기를 확정한다
            ConfigureBehavior(GetComponent<BehaviorParameters>());
            MaxStep = 0; // 에피소드는 구역을 다 놓거나 실패하면 끝난다
            base.OnEnable();
        }

        public override void Initialize()
        {
            _view = GetComponent<LayoutView>();
            _rng = Seed != 0 ? new System.Random(Seed + transform.GetSiblingIndex()) : new System.Random();
        }

        // ------------------------------------------------------------------
        // 에피소드
        // ------------------------------------------------------------------

        public override void OnEpisodeBegin()
        {
            for (int attempt = 0; attempt < 20; attempt++)
            {
                Env.Reset(CreateProblem());
                if (Env.HasValidAction) break;
            }
            _partialCost = Env.PartialCost();

            // 시연 모드에서는 직전 완성안을 EndPause 동안 남겨 둔다
            if (StepInterval <= 0f) RenderIfNeeded(force: false);
        }

        LayoutProblem CreateProblem()
        {
            if (Source == ProblemSource.ProjectInput)
            {
                var input = InputStorage.Load() ?? ProjectInput.CreateSample();
                return LayoutProblem.FromInput(input);
            }
            int zones = Mathf.RoundToInt(Academy.Instance.EnvironmentParameters.GetWithDefault("zone_count", ZoneCount));
            return LayoutProblem.Random(_rng, zones);
        }

        // ------------------------------------------------------------------
        // 관측
        // ------------------------------------------------------------------

        public override void CollectObservations(VectorSensor sensor)
        {
            var p = Env.Problem;
            var z = Env.Current;

            // 현재 구역 종류 (one-hot 5)
            for (int t = 0; t < LayoutProblem.ZoneTypeCount; t++)
                sensor.AddObservation(z != null && (int)z.Zone == t ? 1f : 0f);

            // 현재 구역 크기 (2)
            sensor.AddObservation(z != null ? z.W / (float)LayoutProblem.GridW : 0f);
            sensor.AddObservation(z != null ? z.H / (float)LayoutProblem.GridH : 0f);

            // 현재 구역과 각 구역 종류 / 도크 사이 물동량 비중 (5 + 2)
            float total = p != null ? Mathf.Max(1f, p.TotalFlow) : 1f;
            for (int t = 0; t < LayoutProblem.ZoneTypeCount; t++)
            {
                float f = 0f;
                if (z != null)
                {
                    int me = LayoutProblem.NodeOfZone(Env.CurrentIndex);
                    int other = p.IndexOfZone((LayoutZone)t);
                    if (other >= 0)
                    {
                        int o = LayoutProblem.NodeOfZone(other);
                        f = (p.Flow[me, o] + p.Flow[o, me]) / total;
                    }
                }
                sensor.AddObservation(f);
            }
            for (int dock = 0; dock < 2; dock++)
            {
                float f = 0f;
                if (z != null)
                {
                    int me = LayoutProblem.NodeOfZone(Env.CurrentIndex);
                    f = (p.Flow[me, dock] + p.Flow[dock, me]) / total;
                }
                sensor.AddObservation(f);
            }

            // 진행률, 점유율 (2)
            sensor.AddObservation(p != null && p.Zones.Count > 0 ? Env.CurrentIndex / (float)p.Zones.Count : 0f);
            sensor.AddObservation(Env.FillRatio);
        }

        public override void WriteDiscreteActionMask(IDiscreteActionMask actionMask)
        {
            // 유효한 행동이 하나도 없으면 마스킹하지 않는다 (전부 막으면 정책 출력이 깨짐). 이 경우는 아래에서 실패 처리.
            if (Env.Done || !Env.HasValidAction) return;
            for (int a = 0; a < LayoutEnv.ActionCount; a++)
                if (!Env.IsValid(a)) actionMask.SetActionEnabled(0, a, false);
        }

        // ------------------------------------------------------------------
        // 행동과 보상
        // ------------------------------------------------------------------

        public override void OnActionReceived(ActionBuffers actions)
        {
            if (Env.Done) return;
            int a = actions.DiscreteActions[0];

            if (!Env.Apply(a))
            {
                Finish(success: false);
                return;
            }

            float cost = Env.PartialCost();
            AddReward(-CostWeight * (cost - _partialCost) / Env.CostScale);
            _partialCost = cost;

            if (Env.Done) Finish(success: true);
            else if (!Env.HasValidAction) Finish(success: false);
            else RenderIfNeeded(force: StepInterval > 0f);
        }

        void Finish(bool success)
        {
            EpisodesDone++;
            var stats = Academy.Instance.StatsRecorder;
            stats.Add("Layout/SuccessRate", success ? 1f : 0f);

            if (success)
            {
                AddReward(CompletionBonus);
                LastCost = _partialCost;
                stats.Add("Layout/CostNormalized", _partialCost / Env.CostScale);

                if (CompareWithGreedyEvery > 0 && EpisodesDone % CompareWithGreedyEvery == 0)
                {
                    if (GreedyPlacer.Solve(_greedyEnv, Env.Problem, out float g))
                    {
                        LastGreedyCost = g;
                        // 1보다 작으면 RL이 기준선보다 좋은 안을 만든 것
                        stats.Add("Layout/CostVsGreedy", _partialCost / Mathf.Max(1f, g));
                    }
                }
            }
            else
            {
                AddReward(FailPenalty);
            }

            RenderIfNeeded(force: true, caption: success
                ? $"비용 {_partialCost / 1000f:0.0} 천 단위·m/일" + (LastGreedyCost > 0 ? $"  (기준선 {LastGreedyCost / 1000f:0.0})" : "")
                : "실패: 놓을 곳 없음");

            if (StepInterval > 0f) _pauseUntil = Time.time + EndPause;
            EndEpisode();
        }

        void RenderIfNeeded(bool force, string caption = null)
        {
            if (_view == null) return;
            bool training = Academy.Instance.IsCommunicatorOn;
            if (force || RenderEveryStep || !training) _view.Render(Env, caption);
        }

        /// <summary>Behavior Type = Heuristic Only 일 때: 탐욕 기준선으로 움직인다.</summary>
        public override void Heuristic(in ActionBuffers actionsOut)
        {
            var d = actionsOut.DiscreteActions;
            int a = GreedyPlacer.BestAction(Env, _rng);
            d[0] = a < 0 ? 0 : a;
        }

        // ------------------------------------------------------------------
        // 결정 요청
        // ------------------------------------------------------------------

        void FixedUpdate()
        {
            if (StepInterval <= 0f) { RequestDecision(); return; }
            if (Time.time < _pauseUntil) return;
            _timer += Time.fixedDeltaTime;
            if (_timer < StepInterval) return;
            _timer = 0f;
            RequestDecision();
        }
    }
}
