# 물류센터 레이아웃 추천 시스템 · Unity 프로토타입 (1단계)

대상 버전: Unity 6000.6.8f1

## 실행 방법
1. `git clone`으로 저장소를 받습니다.
2. Unity Hub → **Add project from disk** → 받은 폴더를 선택합니다. 필요한 패키지(Unity UI 등)는 `Packages/manifest.json`에 들어 있어서 처음 열 때 자동으로 설치됩니다.
3. 열린 씬(기본 Untitled 씬도 괜찮습니다)에서 **Play**를 누릅니다.
   씬에 따로 배치할 것은 없습니다. 실행하면 `AppManager`가 자동으로 생성되고, 화면은 모두 코드로 만들어집니다.
4. 이후 업데이트는 `git pull`로 받습니다. Unity가 열려 있어도 자동으로 다시 컴파일됩니다.

처음 열 때 자동 설정 (`Assets/LayoutRecommender/Editor/ProjectSetup.cs`)
- URP와 Input System 패키지를 에디터 버전에 맞게 설치합니다.
- URP 에셋을 만들어 기본 렌더 파이프라인으로 지정합니다. ("Built-in Render Pipeline 지원 중단" 경고가 사라집니다.)
- Active Input Handling을 새 Input System으로 바꾸고 에디터 재시작을 묻습니다. ("Input Manager" 경고가 사라집니다.)
- 다시 실행하려면 메뉴 **Tools → Layout Recommender → 프로젝트 설정 다시 실행**을 누릅니다.

참고
- 한글은 OS 폰트(맑은 고딕 등)를 사용합니다. 글자가 □로 보이면 알려주세요. 폰트 에셋을 넣는 방식으로 바꾸겠습니다.
- 다른 씬에서 자동 실행을 끄려면 그 씬에 `DisableLayoutRecommender`라는 이름의 빈 오브젝트를 두면 됩니다.

## 1단계에서 구현된 것
| 화면 | 내용 |
|---|---|
| 시작화면 | 새 프로젝트 시작 / 이전 입력 불러오기 / 종료 |
| 입력 절차 (5단계) | ① 화물 특성(파렛트·토트·카톤, L/W/H, 중량, SKU, 단적) ② 입출고량(일 입출고, 피크 계수, 가동시간, 보관 필요량) ③ 건물 조건(폭·깊이·층고·도크) ④ 설비 선호(자동화 수준, 설비별 사용/제외, 적재 단위에 맞지 않는 설비는 자동 비활성) ⑤ 확인 |
| 입력 검증 | 단계마다 값을 확인합니다. 피크 시간당 처리량, 예상 적재 단수 같은 파생값을 실시간으로 보여줍니다. |
| 저장 | '레이아웃 생성'을 누르면 입력값이 `persistentDataPath/last_input.json`에 저장됩니다. |
| 레이아웃 화면 HUD 틀 | 좌측: 카테고리별 설비 팔레트(보관/운반/이송/인력) · 우측: 선택한 개체의 속성창 · 상단: 시뮬레이션 시간, ▶/∥/■, 배속 · 하단: 진행률과 로그 · 가운데: 건물 크기에 맞춘 3D 바닥 |

## 폴더 구조
```
Assets/LayoutRecommender/Scripts
├─ Core/AppManager.cs          앱 부트스트랩, 화면 전환, 카메라/이벤트시스템
├─ Data/ProjectInput.cs        입력 모델(화물/입출고/건물/설비선호), 검증, 저장
├─ Data/EquipmentCatalog.cs    설비 10종 정의(적용 단위, 점유 크기, 속도, 처리능력 대표값)
├─ UI/UIFactory.cs             코드로 UGUI를 만드는 도우미, 테마 색
└─ Screens/
   ├─ ScreenBase.cs
   ├─ StartScreen.cs
   ├─ InputWizardScreen.cs
   └─ LayoutScreen.cs          HUD 틀 (2~3단계에서 자동 배치/시뮬레이션 연결)
```

## 다음 단계
- 2단계: 추천 로직으로 배치안 2~3개 생성. 좌측 팔레트에서 설비를 끌어다 놓는 자동 배치 애니메이션 구현.
- 3단계: 입고·보관·출고 흐름 시뮬레이션, 처리량·가동률·병목 지표, 안별 비교와 추천.
- 4단계(선택): 사용자 드래그 배치 수정 후 재시뮬레이션.

설비의 처리능력과 점유 크기는 프로토타입용 대표값입니다. `EquipmentCatalog.cs`에서 고칠 수 있습니다.
