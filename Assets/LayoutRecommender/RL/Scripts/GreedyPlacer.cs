using System.Collections.Generic;

namespace LayoutRecommender.RL
{
    /// <summary>
    /// 기준선(baseline) 배치: 매 스텝 비용이 가장 적게 늘어나는 위치를 고른다.
    /// - 강화학습 결과를 비교하는 기준
    /// - Behavior Type을 Heuristic Only로 두면 에이전트가 이 방식으로 움직인다 (학습 전 동작 확인용)
    /// </summary>
    public static class GreedyPlacer
    {
        /// <summary>현재 구역에 대한 탐욕 행동. 유효한 행동이 없으면 -1.</summary>
        public static int BestAction(LayoutEnv env, System.Random tieBreak = null)
        {
            if (env.Done || !env.HasValidAction) return -1;
            var candidates = new List<int>();
            for (int a = 0; a < LayoutEnv.ActionCount; a++)
                if (env.IsValid(a)) candidates.Add(a);

            int best = -1;
            float bestCost = float.MaxValue;
            int ties = 0;
            foreach (var a in candidates)
            {
                float c = env.CostIfPlaced(a);
                if (c < bestCost - 1e-3f) { bestCost = c; best = a; ties = 1; }
                else if (tieBreak != null && c <= bestCost + 1e-3f && tieBreak.Next(++ties) == 0) best = a;
            }
            return best;
        }

        /// <summary>문제 전체를 탐욕 배치로 푼다. 성공하면 true, 최종 비용은 cost.</summary>
        public static bool Solve(LayoutEnv env, LayoutProblem problem, out float cost, System.Random tieBreak = null)
        {
            env.Reset(problem);
            while (!env.Done)
            {
                int a = BestAction(env, tieBreak);
                if (a < 0 || !env.Apply(a)) { cost = float.MaxValue; return false; }
            }
            cost = env.PartialCost();
            return true;
        }
    }
}
