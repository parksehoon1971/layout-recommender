# 강화학습(ML-Agents)으로 레이아웃 자동 생성

물류센터 구역(입고 · 보관 · 피킹 · 포장 · 출고)을 격자 위에 **하나씩 순서대로** 놓는 에이전트를 PPO로 학습합니다.
지금은 최소 버전으로, 16×10 격자에 구역 3~5개를 배치합니다.

## 문제 정의

| 항목 | 내용 | 코드 |
|---|---|---|
| 환경 | 건물을 16×10 격자로 나눔. 왼쪽 벽에 입고 도크, 오른쪽 벽에 출고 도크, 기둥은 통행·배치 불가 | `RL/Scripts/LayoutEnv.cs` |
| 문제 | 학습: 매 에피소드 구역 크기·물동량·도크 위치·기둥을 무작위로 바꿈 / 실제 입력: `ProjectInput`(화물·입출고량·건물·설비 선호)에서 구역 면적과 물동량을 계산 | `RL/Scripts/LayoutProblem.cs` |
| 상태 | 격자 이미지 16×10×11채널 (기둥, 입고/출고 도크, 점유, 구역 종류별 점유 5, 현재 구역의 유효 위치 2) + 벡터 16 (현재 구역 종류·크기, 다른 구역·도크와의 물동량 비중, 진행률, 점유율) | `RL/Scripts/LayoutGridSensor.cs`, `LayoutPlacementAgent.CollectObservations` |
| 행동 | 이산 320개 = 위치 160 × 회전 2. 겹치는 위치, 그리고 놓았을 때 도크·구역 중 하나라도 통로와 끊기는 위치는 **action masking**으로 막음 | `LayoutPlacementAgent.WriteDiscreteActionMask` |
| 보상 | 스텝마다 −4 × (물동량×통로거리 증가분 / 정규화값), 완성 시 +1, 놓을 곳이 없으면 −1. 통로거리는 빈 칸을 따라가는 최단거리(BFS) | `LayoutPlacementAgent.OnActionReceived` |
| 기준선 | 매 스텝 비용 증가가 가장 작은 위치를 고르는 탐욕 배치. 학습 결과와 비교하고, 학습 전 동작 확인(Heuristic)에도 씀 | `RL/Scripts/GreedyPlacer.cs` |

참고 수치 (같은 코드를 에디터 밖에서 돌려 확인): 무작위 배치의 정규화 비용 ≈ 0.25, 탐욕 기준선 ≈ 0.06.
보상으로 바꾸면 무작위 ≈ 0, 기준선 ≈ 0.75입니다. 학습 목표는 기준선보다 높은 보상, 즉 TensorBoard의 `Layout/CostVsGreedy`가 1보다 작아지는 것입니다.

## 1. 설치 (한 번만)

**Unity 쪽**: `Packages/manifest.json`에 `com.unity.ml-agents` 4.2.0을 넣어 두었습니다. 저장소를 받고 Unity를 열면 자동으로 설치됩니다.
Package Manager에서 버전 오류가 나면 **Window → Package Manager → + → Add package by name**에 `com.unity.ml-agents`를 입력해 에디터에 맞는 버전을 설치하세요.

**Python 쪽** (Anaconda 또는 Miniconda 필요):

```bash
conda create -n mlagents python=3.10.12
conda activate mlagents
# Windows에서 NVIDIA GPU를 쓰려면 먼저:
pip3 install torch~=2.2.1 --index-url https://download.pytorch.org/whl/cu121
python -m pip install mlagents==1.1.0
mlagents-learn --help   # 도움말이 나오면 설치 완료
```

## 2. 씬 만들기

Unity 메뉴 **Tools → Layout Recommender → 강화학습**

- **학습 씬 만들기** → `Assets/LayoutRecommender/RL/Scenes/LayoutRL_Training.unity`
  에이전트 12개(4×3)가 동시에 무작위 문제를 풉니다.
- **시연 씬 만들기** → `Assets/LayoutRecommender/RL/Scenes/LayoutRL_Demo.unity`
  앱에서 마지막으로 저장한 입력값(`last_input.json`, 없으면 샘플값)으로 0.4초마다 구역을 하나씩 놓아 보여줍니다.

학습 전에도 아무 씬이나 열고 Play를 누르면 됩니다. 연결된 학습기도 모델도 없으면 탐욕 기준선으로 움직이므로 환경이 제대로 도는지 바로 볼 수 있습니다.

## 3. 학습

저장소 폴더에서:

```bash
conda activate mlagents
mlagents-learn ml-agents/layout_ppo.yaml --run-id=layout01
```

"Start training by pressing the Play button in the Unity Editor"가 나오면 `LayoutRL_Training` 씬을 열고 **Play**를 누릅니다.

- 진행 확인: `tensorboard --logdir results` → http://localhost:6006
  - `Environment/Cumulative Reward`: 보상 (기준선 ≈ 0.75)
  - `Layout/SuccessRate`: 끝까지 배치한 비율
  - `Layout/CostVsGreedy`: 같은 문제에서 탐욕 기준선 대비 비용 (1보다 작으면 RL이 더 좋음)
  - `Environment/Lesson Number/zone_count`: 커리큘럼 단계 (구역 3 → 4 → 5개)
- 멈췄다가 이어서: `mlagents-learn ml-agents/layout_ppo.yaml --run-id=layout01 --resume`
- 더 빠르게: Unity에서 빌드한 실행 파일로 `--env=빌드경로 --num-envs=4 --no-graphics`

## 4. 학습된 모델 쓰기

1. 학습이 끝나면 `results/layout01/LayoutPlacer.onnx`가 생깁니다.
2. 이 파일을 `Assets/` 아래로 복사합니다.
3. `LayoutRL_Demo` 씬에서 `LayoutAgent_0` → **Behavior Parameters → Model**에 끌어다 놓고, Behavior Type을 **Inference Only**로 바꿔 Play 합니다.
   화면 아래에 RL 안의 비용과 기준선 비용이 함께 표시됩니다.

## 조정할 곳

- 보상 가중치: `LayoutPlacementAgent`의 `CostWeight`, `CompletionBonus`, `FailPenalty` (인스펙터에서 변경 가능)
- 구역 면적 산정식: `LayoutProblem.FromInput` (프로토타입용 대략값)
- 격자 크기: `LayoutProblem.GridW/GridH`. 바꾸면 신경망 입력 크기가 바뀌므로 처음부터 다시 학습해야 합니다.

## 다음 단계

- 앱의 레이아웃 화면 **자동 배치** 버튼은 지금 탐욕 기준선을 씁니다. 학습된 모델을 이 버튼에 연결합니다.
- 보상에 면적 활용률, 통로 폭(지게차 3.5m 등 설비별), 입고→출고 흐름 방향 위반 페널티를 추가합니다.
- 구역 대신 설비 단위(랙 열, 컨베이어 모듈)로 세분화하고, RL 초안을 국소 탐색으로 다듬는 하이브리드를 붙입니다.
