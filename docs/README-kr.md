## QuickSort (pasta.quicksort)

[**English(Support)**](https://github.com/p-asta/lc-quicksort/blob/main/README.md) | **한국어** <br/>
Lethal Company용 함선·크루저 아이템 정렬, 빠른 이동 명령어, 프로필 모드입니다.

## 게스트(클라이언트) 사용 시 중요 안내
게스트(클라이언트)로 플레이할 때, **[TooManyItems](https://thunderstore.io/c/lethal-company/p/mattymatty/TooManyItems/)** 를 설치하면 **일부 아이템이 정렬되지 않거나(스냅백)** 하는 문제가 없엘 수 있습니다.

## 명령어
- **팁**: 명령어에 **`[itemName]`**처럼 대괄호로 표기되어 있으면 **아이템명은 선택사항**입니다. 생략하면(가능한 경우) **현재 손에 들고 있는 아이템**을 기준으로 동작합니다.
- **`/sort`**: 함선과 크루저의 정렬 가능한 아이템을 함선으로 가져와 전체 정렬합니다.
  - 전체 정렬은 `skippedItems`를 스킵 리스트로 사용합니다.
  - 손에 들고 있는 아이템은 그대로 유지합니다.
- **`/sort -a`**: 전체 정렬 + **`skippedItems` 무시** (정렬 가능한 것 전부)
- **`/sort -b`**: 전체 정렬 + “저장 위치 우선”
  - 어떤 타입에 `/sort set` 저장 위치가 있으면, 그 타입은 `skippedItems`에 걸려도 **스킵되지 않습니다**.
  - 크루저에 있는 해당 타입도 저장된 **함선** 위치로 가져옵니다. `/css`의 크루저 위치는 함선에서 놓을 위치를 정하거나 `skippedItems`를 무시하는 기준이 되지 않습니다.
  - 저장 위치가 없는 타입은 기존처럼 `skippedItems`가 적용됩니다.
  - **주의**: `-a`와 `-b`는 같이 쓸 수 없습니다 (`/sort -ab` / `/sort -ba`도 거부됨).
- **`/sort <itemName>`**: 해당 아이템 “타입”을 내 위치로 끌어옵니다. (예: `/sort cash_register`, `/sort weed killer`, `/sort wee`)
  - 이 명령은 **스킵 리스트를 무시**하므로 `skippedItems`에 있어도 동작합니다.
- **`/sort <number>`**: 해당 숫자에 바인딩된 아이템 타입을 내 위치로 끌어옵니다. (예: `/sort 1`)
- **`/pile [itemName]`**: `/sort <itemName>`처럼 특정 타입을 내 위치로 끌어오되, **아이템명을 생략하면 손에 든 아이템 타입을 사용하고 손에 든 것도 함께 이동**합니다.

### 스킵 리스트 (`skippedItems`)
게임 내 채팅 명령으로 `skippedItems`를 편집할 수 있습니다.
- **`/sort skip list`**: 현재 `skippedItems` 토큰 목록 보기
- **`/sort skip add [itemName|alias|id]`**: 토큰 추가
  - 생략하면 **손에 든 아이템**을 사용합니다.
  - alias(별칭) 또는 shortcut id(숫자)도 입력 가능
- **`/sort skip remove [itemName|alias|id]`**: 토큰 제거
  - 생략하면 **손에 든 아이템**을 사용합니다.
  - alias(별칭) 또는 shortcut id(숫자)도 입력 가능

### 바인딩 (숫자 shortcut + alias)
현재 **손에 들고 있는 아이템**을 바인딩합니다.
- **`/sort bind <name|id>`**
  - **`/sort bind 1`** → 손에 든 아이템을 shortcut id 1에 바인딩
  - **`/sort bind meds`** → 손에 든 아이템을 alias `meds`에 바인딩
- **`/sort bind reset <name|id>`**
  - **`/sort bind reset 1`** → shortcut id 1 바인딩 제거
  - **`/sort bind reset meds`** → alias `meds` 바인딩 제거
- **`/sb <name|id>`**: `/sort bind ...`의 단축 명령
- **`/sb reset <name|id>`**: `/sort bind reset ...`의 단축 명령

바인딩 목록 보기:
- **`/sort bindings`** (`/sort binds`, `/sort shortcuts`, `/sort aliases`도 가능)

바인딩 사용:
- **`/sort 1`** (숫자 바인딩)
- **`/sort meds`** (alias 바인딩)

### 저장 위치 (Saved positions)
- **`/sort set [itemName]`**: 해당 타입의 정렬 위치를 내 현재 위치로 저장합니다 (**부분일치 지원**).
- **`/ss [itemName]`**: `/sort set ...` 단축 명령 (**부분일치 지원**).
- `/ss`의 함선 위치와 `/css`의 크루저 위치는 같은 아이템 타입이어도 별도로 저장됩니다. `/sort -b`는 함선 위치를, `/cs`는 크루저 위치와 최대 개수를 사용합니다.
- **`/sort reset [itemName]`**: 저장된 위치를 삭제합니다.
- **`/sr [itemName]`**: `/sort reset ...` 단축 명령
- **`/sort positions`**: 저장된 위치 목록을 출력합니다.
- **`/sp`**: `/sort positions` 단축 명령
- **`/sbl`**: `/sort bindings` 단축 명령
- **`/sk ...`**: `/sort skip ...` 단축 명령 (예: `/sk list`, `/sk add ...`, `/sk remove ...`)

### 크루저 정렬

- **`/css <구역> [아이템명] [최대 개수]`**: 크루저 위 또는 근처에서 해당 아이템의 선반 구역을 저장하고, 같은 아이템을 최대 개수까지 즉시 그곳에 배치합니다. 아이템명을 생략하면 손에 든 아이템을 사용하고, 최대 개수를 생략하면 **10개**가 기본값입니다(설정 가능 범위: 1–10000). 예: `/css A2 shotgun`은 선반 A2 구역에 산탄총을 최대 10개, `/css A2 shotgun 2`는 최대 2개 배치합니다.
- **`/css [아이템명] [최대 개수]`**: 크루저 위에 서서 선반 구역 대신 현재 위치를 저장하고 같은 아이템을 즉시 배치합니다. 종류가 같고 자리가 남아 있으면 손에 든 아이템도 내려놓습니다. 아이템명은 손에 든 아이템, 최대 개수는 **10개**가 기본값입니다.
- **선반 명령의 다른 입력 방식**: `/css shelf A2 shotgun 2` 또는 CruiserLoader 방식의 `/css shotgun 2 A2`도 `/css A2 shotgun 2`와 같은 선반 설정을 저장합니다.
- **`/css zones`**: 사용 가능한 선반 구역을 표시합니다. A·B·C·E·F·G에는 각 3단 선반(`A1`–`A3` 등)이 있고, `D1`–`D3`는 중앙 배치 지점입니다. `D2`는 레이더 부스터 자리이며 아이템 한 개만 놓을 수 있습니다. 구역 선택 시 [CruiserLoader의 구역 배치 그림](https://github.com/veber01/LC-CruiserLoader/blob/main/ZoneLayout.png)을 참고하세요.
- **`/css list`**: 저장된 크루저 위치와 최대 개수를 표시합니다.
- **`/css reset [아이템명]`**: 저장된 크루저 위치를 삭제합니다. 아이템명을 생략하면 손에 든 아이템을 사용합니다.
- **`/csort`**: 가장 가까운 크루저에 이미 있는 설정된 아이템을 저장된 선반 구역까지 포함해 정리하고, 함선에 있는 같은 아이템을 각 타입의 저장된 최대 개수까지 싣습니다. 크루저 위에 서 있지 않아도 실행할 수 있습니다. 최대 개수를 초과하는 아이템은 원래 위치에 남습니다.
- **`/cs`**: `/csort`의 단축 명령입니다.

`/css`는 지정한 크루저 위치의 **크루저 기준 Y 좌표에 0.3을 더해 저장**합니다. 이전에 수동으로 저장한 크루저 위치는 그 자리에서 `/css`를 다시 실행하기 전까지 기존 Y 값이 유지됩니다. 같은 종류의 아이템 여러 개를 배치할 때 `/css`와 `/cs`는 X/Z 위치를 고정하고, 아이템마다 `sameTypeStackStepY` 설정값만큼 Y만 올립니다(기본값 **0**, 정확히 겹침). 선반 구역과 최대 개수는 QuickSort의 크루저 위치 설정에 저장되며 QuickSort 프로필에도 포함됩니다. `/cs`는 저장된 설정을 사용해 정렬합니다. 선반 구역 하나에는 그 구역을 공유하는 모든 아이템을 합쳐 **최대 20개**를 놓을 수 있고, `D2`에는 **1개**만 놓을 수 있습니다. `[최대 개수]`에는 10000까지 입력할 수 있지만, 실제 배치는 구역 수용량에서 멈추며 한도에 도달하면 정렬 결과에 표시됩니다. 선반 배치 시 아이템을 떨어뜨리는 애니메이션 없이 목표 위치로 바로 옮겨, 정렬 중 아이템끼리 밀릴 가능성을 줄입니다.

선반에 놓인 아이템은 선반에 있는 동안 크루저에 상대적인 위치에 고정되고, 집어 들거나 선반에서 빼면 고정이 해제됩니다. 멀티플레이에서 물리 고정까지 적용하려면 아이템 권한을 가진 쪽(보통 호스트)에 QuickSort가 필요합니다. 일부 플레이어만 QuickSort를 사용해도 직접 배치는 동작합니다.

크루저 선반 배치 기능은 [veber01의 LC-CruiserLoader](https://github.com/veber01/LC-CruiserLoader)를 참고하여 개발했습니다.

### 프로필

프로필에는 함선 정렬 설정(`skippedItems` 포함), 함선 저장 위치, 크루저 저장 위치와 최대 개수가 들어갑니다. 프로필 저장 후 바꾼 설정을 반영하려면 같은 이름으로 다시 저장해야 합니다. 내 기본 설정은 내장 `default` 프로필로 사용할 수 있습니다.

로비에서 호스트 프로필을 받을 수 있으면 임시 `host` 프로필로 표시됩니다. BepInEx의 **Sync Host Profile** 설정은 기본적으로 켜져 있어 호스트 프로필을 자동 적용합니다. 이 설정을 끄면 내 프로필을 유지하며, 필요할 때 `/pu host`로 수동 적용할 수 있습니다. 로비를 나가면 이전에 선택한 내 프로필과 실제 설정이 복원됩니다.

- **`/profile save [이름]`**: 현재 설정과 위치를 저장합니다. 이름을 생략하면 `default`에 저장하며, 같은 이름으로 다시 저장하면 갱신합니다.
- **`/profile use [이름]`**: 저장된 프로필을 적용합니다. 이름을 생략하면 `default`를 사용합니다. 호스트 프로필이 있으면 `/pu host`로 호스트 설정을 적용할 수 있습니다.
- **`/profile sort [이름]`**: 이름이 있으면 해당 프로필을 적용한 뒤 함선을 정렬합니다.
- **`/profile csort [이름]`**: 이름이 있으면 해당 프로필을 적용한 뒤 크루저를 정렬합니다.
- **`/profile list`**: 저장된 프로필 목록과 현재 활성 프로필을 표시합니다.
- **`/profile delete <이름>`**: 저장된 프로필을 삭제합니다.
- **단축 명령**: `/ps [이름]` = `/profile save [이름]`, `/pu [이름]` = `/profile use [이름]`, `/pl` = `/profile list`, `/pd <이름>` = `/profile delete <이름>`.

## 설정 / 파일
모든 파일은 `BepInEx/config` 아래에 생성됩니다.
- **바인딩**: `pasta.quicksort.sort.bindings.json`
- **저장 위치**: `pasta.quicksort.sort.positions.json`
- **크루저 위치와 최대 개수**: `pasta.quicksort.cruiser.positions.json`
- **프로필**: `pasta.quicksort.profiles.json`
- **임시 호스트 프로필 복구**: 호스트 프로필 적용 중 `pasta.quicksort.host-recovery.json`을 만들고 개인 설정 복원 후 삭제합니다. 세션이 비정상 종료된 경우 이 파일로 개인 설정을 복구합니다.
- **호스트 프로필 동기화**: BepInEx의 **Sync Host Profile** 설정(기본값 켜짐). 끄면 임시 호스트 프로필을 자동 적용하지 않습니다.

## 참고
- **아이템명 정규화**: 매칭을 위해 공백/하이픈은 언더스코어로 정규화됩니다. (예: `kitchen knife` → `kitchen_knife`)
- **한국어 패치(로컬라이즈) 호환**: 일부 한국어 아이템명 입력도 자동으로 영문 키로 인식합니다. 예: `머그잔`→`coffee_mug`, `쿠키 틀`→`cookie_mold_pan`, `식칼`→`kitchen_knife`, `산탄총`→`shotgun`.
- **기본 입력 alias**:
  - `double_barrel` → `shotgun`
  - `shotgun_shell` → `ammo`
- **특정 아이템 Y 오프셋(조금 더 낮게 배치)**:
  - 전체 정렬(`/sort`) 시 아래 타입은 기본보다 조금 더 낮게 놓습니다: `toilet_paper`, `chemical_jug`, `cash_register`, `fancy_lamp`, `large_axle`, `v_type_engine`
- **명시적 끌어오기는 스킵 무시**: `/sort <itemName>`는 해당 타입이 `skippedItems`에 있어도 동작합니다.
- **레거시 설정 자동 수정**:
  - `skippedItems`에 예전 오타 `rader_booster`가 있으면 `radar_booster`로 자동 교정됩니다.
  - 토큰에 앞/뒤로 `_`가 붙어있으면(예: `_kitchen_knife`) 정규화됩니다.
- **설정 마이그레이션(0.1.5)**:
  - `configVersion`이 없거나 `0.1.5` 미만이고, `sortOriginY` 값이 `0.5`라면 `0.1`로 자동 변경됩니다.
- **스킵 목록 유지**: 직접 추가한 `shotgun`, `ammo` 토큰은 설정 마이그레이션 후에도 유지됩니다.

## SS
![alt text](https://raw.githubusercontent.com/P-Asta/lc-QuickSort/refs/heads/main/assets/image.png)
