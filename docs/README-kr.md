## QuickSort (pasta.quicksort)

[**English(Support)**](https://github.com/p-asta/lc-quicksort/blob/main/README.md) | **한국어** <br/>
Lethal Company용 함선 아이템 정렬 + 빠른 이동(끌어오기) 명령어 모드입니다.

## 게스트(클라이언트) 사용 시 중요 안내
게스트(클라이언트)로 플레이할 때, **[TooManyItems](https://thunderstore.io/c/lethal-company/p/mattymatty/TooManyItems/)** 를 설치하면 **일부 아이템이 정렬되지 않거나(스냅백)** 하는 문제가 없엘 수 있습니다.

## 명령어
- **팁**: 명령어에 **`[itemName]`**처럼 대괄호로 표기되어 있으면 **아이템명은 선택사항**입니다. 생략하면(가능한 경우) **현재 손에 들고 있는 아이템**을 기준으로 동작합니다.
- **`/sort`**: 전체 정렬 (함선 내 아이템 전부 정렬)
  - 전체 정렬은 `skippedItems`를 스킵 리스트로 사용합니다.
- **`/sort -a`**: 전체 정렬 + **`skippedItems` 무시** (정렬 가능한 것 전부)
- **`/sort -b`**: 전체 정렬 + “저장 위치 우선”
  - 어떤 타입에 `/sort set` 저장 위치가 있으면, 그 타입은 `skippedItems`에 걸려도 **스킵되지 않습니다**.
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
- **`/sort reset [itemName]`**: 저장된 위치를 삭제합니다.
- **`/sr [itemName]`**: `/sort reset ...` 단축 명령
- **`/sort positions`**: 저장된 위치 목록을 출력합니다.
- **`/sp`**: `/sort positions` 단축 명령
- **`/sbl`**: `/sort bindings` 단축 명령
- **`/sk ...`**: `/sort skip ...` 단축 명령 (예: `/sk list`, `/sk add ...`, `/sk remove ...`)

## 설정 / 파일
모든 파일은 `BepInEx/config` 아래에 생성됩니다.
- **바인딩**: `pasta.quicksort.sort.bindings.json`
- **저장 위치**: `pasta.quicksort.sort.positions.json`

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
  - `skippedItems`에 `shotgun`, `ammo` 토큰이 없으면 자동으로 추가됩니다.
- **설정 마이그레이션(0.1.7)**:
  - `skippedItems`가 실수로 `shotgun, ammo`만 들어있는 경우, 기본 목록으로 되돌립니다.

## SS
![alt text](https://raw.githubusercontent.com/P-Asta/lc-QuickSort/refs/heads/main/assets/image.png)