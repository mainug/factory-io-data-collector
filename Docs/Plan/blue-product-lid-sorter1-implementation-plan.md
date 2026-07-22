# Blue Product Lid Sorter 1 자동 분류 구현 계획

## 1. 목적

Blue Raw Material을 가공하여 생성된 `Blue Product Lid`가 Sorter 1에 도착하면, Sorter 1을 오른쪽 이송 상태로 구동하여 Blue Lid 경로로 자동 배출한다.

이 문서는 코드 구현 전에 Factory I/O 작업자와 C# 개발자가 확정해야 할 I/O 계약, 동작 순서, 안전 조건, 구현 단계 및 완료 기준을 정의한다.

## 2. 구현 범위

### 포함

- Blue Lid 제품 판별
- 가공 완료 제품의 Sorter 1 도착 감지
- Sorter 1 오른쪽 이송 출력 제어
- 센서 상승/하강 에지 기반 제품 1회 처리
- 동작 제한시간과 통신 오류 시 안전 정지
- 설비 상태 및 공정 이벤트 기록
- Mock 및 실제 Modbus 모드 검증

### 제외

- Sorter 2 제어
- Base 제품의 최종 분류 자동화
- 여러 제품이 동시에 Sorter 구간에 들어오는 다중 제품 추적
- Factory I/O 씬의 기계 구조 자체 변경
- PLC 프로그램 작성

## 3. 현재 상태 진단

현재 Factory I/O Modbus TCP Server에는 가공기 도입부와 배출 벨트가 다음과 같이 매핑되어 있으며, C#의 현재 임시 주소와 일치한다.

| 종류 | 주소 | Factory I/O 태그 |
|---|---:|---|
| Input | 0 | Stop Entrance Belt Sensor |
| Input | 1 | Machining Center (Is Busy) |
| Input | 2 | Machining Center (Has Error) |
| Input | 3 | Machining Center (Opened) |
| Input | 4 | Write Sensor |
| Coil | 0 | Entrance belt |
| Coil | 1 | Machining Center (1 Lids 0 Base) |
| Coil | 2 | Machining Center (Start) |
| Coil | 3 | Machining Center (Stop) |
| Coil | 4 | Machining Center (Reset) |
| Coil | 5 | Exit Belt Sorter 1 |
| Input Register | 0 | Machining Center (Progress) |

현재 C# 서비스는 위 가공기 신호만 제어·감시한다. `Blue Lid Camera`, `Read Sensor Sorter 1`, Sorter 1 출력은 서비스 정의와 자동 운전 로직에 포함되어 있지 않으므로 현재 코드만으로는 목표 동작을 수행할 수 없다.

## 4. 최종 I/O 계약

기존 주소 `Input 0~4`, `Coil 0~5`, `Input Register 0`은 변경하지 않는다. 아래 주소를 추가한다.

### 필수 입력

| 주소 | 태그 | 역할 |
|---|---|---|
| Input 5 | Read Sensor Sorter 1 | 제품의 Sorter 1 도착 및 이탈 감지 |
| Input 6 | Blue Lid Camera | Blue Product Lid 판정 |
| Input 7 | Green Lid Camera | Green Lid 오분류 방지 및 상태 확인 |

### 필수 출력

| 주소 | 태그 | 역할 |
|---|---|---|
| Coil 6 | Sorter 1 Forward and Power | Sorter 1 진행 방향 및 동력 구동 |
| Coil 7 | Sorter 1 Blue Lid | Blue Lid 분류 구간 선택/구동 |
| Coil 8 | Sorter 1 Green Lid | Green Lid 분류 구간 선택/구동 |

### 선택 입력

향후 Base 제품까지 확장할 경우 다음 주소를 미리 사용할 수 있다.

| 주소 | 태그 |
|---|---|
| Input 8 | Blue Base Camera |
| Input 9 | Green Base Camera |

## 5. 코드 구현 전 Factory I/O 작업자 선행 작업

1. Factory I/O를 정지하고 모든 Sorter 출력을 OFF로 만든다.
2. Modbus TCP Server I/O Points에서 위 최종 I/O 계약대로 센서와 액추에이터를 매핑한다.
3. 기존 가공기 주소는 이동하거나 덮어쓰지 않는다.
4. 변경된 씬을 원본과 구분되는 새 파일로 저장한다.
5. 드라이버 설정을 다음 값으로 확인한다.
   - IP: `210.119.14.67`
   - Port: `502`
   - Slave ID: `1`
   - 주소 기준: 0-based
6. 낮은 Time Scale과 제품이 없는 상태에서 Sorter 출력의 실제 동작을 시험한다.
7. 다음 출력 조합을 각각 시험하고 이동 방향을 기록한다.

| 시험 | Coil 6 | Coil 7 | Coil 8 | 확인 목적 |
|---|---|---|---|---|
| A | ON | OFF | OFF | 기본 전진/동력 동작 |
| B | ON | ON | OFF | Blue Lid 오른쪽 이송 여부 |
| C | ON | OFF | ON | Green Lid 이동 방향 |

8. 검증 전에는 Coil 7과 Coil 8을 동시에 ON하지 않는다.
9. `Read Sensor Sorter 1`이 제품 도착 시 ON, 완전 이탈 시 OFF 되는지 확인한다.
10. 오른쪽 수신 컨베이어가 별도 구동을 필요로 하는지 확인한다. 별도 액추에이터가 필요하면 태그명과 추가 Coil 주소를 개발자에게 전달한다.
11. 최종 I/O Points 화면과 출력 시험 결과를 캡처하여 개발자에게 전달한다.

## 6. 90도 회전에 대한 제어 원칙

Sorter 1에는 각도를 쓰는 Numeric 출력이나 각도 피드백이 없다. 따라서 C#에서 `90`이라는 각도값을 전송하지 않는다.

Factory I/O 기구가 Boolean 출력 조합을 통해 고정된 오른쪽 이송 동작을 수행하도록 구성되어 있으므로, 작업자가 확인한 출력 조합을 코드의 `Blue Lid Route` 명령으로 사용한다. 현재 예상 조합은 다음과 같지만 실동작 시험 후 확정한다.

```text
Sorter 1 Forward and Power = ON
Sorter 1 Blue Lid          = ON
Sorter 1 Green Lid         = OFF
```

## 7. 목표 자동 운전 시퀀스

```text
자동 분류 활성화
    ↓
Blue Lid Camera 상승 에지 감지
    ↓
제품 종류를 BlueProductLid로 래치
    ↓
Write Sensor에서 가공품 배출 확인
    ↓
Exit Belt Sorter 1로 제품 이송
    ↓
Read Sensor Sorter 1 상승 에지 감지
    ↓
Green Lid 출력 OFF 확인
    ↓
검증된 Blue Lid 오른쪽 이송 출력 ON
    ↓
Read Sensor Sorter 1 하강 에지 또는 정상 이탈 확인
    ↓
Sorter 출력 OFF
    ↓
제품 래치 초기화 후 다음 제품 대기
```

카메라와 Write Sensor의 실제 배치 순서가 위 순서와 다르면 현장 센서 발생 순서를 기준으로 상태 전이를 조정한다.

## 8. 상태 머신 설계

| 상태 | 설명 | 다음 상태 조건 |
|---|---|---|
| Idle | 제품 대기 | Blue Lid Camera 상승 에지 |
| BlueLidDetected | Blue Lid 정보 래치 | 가공 완료/배출 확인 |
| MovingToSorter1 | Exit Belt로 이송 | Read Sensor Sorter 1 상승 에지 |
| DivertingRight | Sorter 오른쪽 이송 출력 ON | 센서 하강 에지 |
| Completed | 출력 OFF 및 이벤트 기록 | 초기화 후 Idle |
| Fault | 시간초과, 상충 출력, 통신 오류 | 안전 정지 및 작업자 Reset |

제품 종류는 단순한 현재 센서값이 아니라 한 제품 주기 동안 유지되는 래치 값으로 관리한다. 센서 신호는 상승/하강 에지로 처리하여 한 제품을 여러 번 분류하지 않도록 한다.

## 9. 안전 및 인터록 조건

- `Sorter 1 Blue Lid`와 `Sorter 1 Green Lid`의 동시 ON을 금지한다.
- 통신 단절, 폴링 실패, 취소 또는 예외 발생 시 Sorter 관련 출력을 OFF한다.
- Sorter 도착 대기와 이탈 대기에 각각 제한시간을 둔다.
- 제한시간 초과 시 자동 재시도하지 않고 Fault 이벤트를 발생시킨다.
- 가공기 오류 신호가 ON이면 새 제품의 자동 분류 시퀀스를 시작하지 않는다.
- 제품 종류가 확정되지 않은 경우 Blue Lid 경로를 임의로 작동시키지 않는다.
- 한 제품의 분류가 끝나기 전에는 다음 제품을 같은 추적 상태에 덮어쓰지 않는다.
- 자동 운전 비활성화 또는 화면 종료와 무관하게 안전 종료 경로가 동작해야 한다.

## 10. 코드 구현 계획

### 1단계: 주소 및 태그 모델 확장

- `FactoryIoMap`에 Sorter 1 입력·출력 정의를 추가한다.
- 주소 숫자를 UI나 제어 코드에 직접 작성하지 않고 정의 객체를 통해 참조한다.
- 실제 Modbus 서비스의 허용 설비 목록에 새 태그를 등록한다.
- Mock 서비스에도 동일한 태그와 기본 상태를 추가한다.

### 2단계: 센서 수집 개선

- Sorter 센서를 폴링 대상에 추가한다.
- 현재 1초 폴링으로 짧은 센서 신호를 놓치지 않도록 실제 운전 주기를 확인하여 약 `50~100ms` 또는 적절한 묶음 읽기로 조정한다.
- 센서별 이전 값을 저장하여 상승/하강 에지를 생성한다.

### 3단계: 분류 제어기 구현

- UI가 아닌 서비스 계층에 Sorter 1 상태 머신을 구현한다.
- Blue Lid 제품 종류를 래치한다.
- 검증된 출력 순서로 Coil 6~8을 제어한다.
- 정상 완료, 시간초과, 통신 오류 시 항상 안전 종료 메서드를 호출한다.

### 4단계: 상태 및 이벤트 제공

- 현재 제품 종류, 분류 상태, Sorter 출력 상태를 상태 스냅샷에 반영한다.
- Blue Lid 감지, Sorter 도착, 오른쪽 배출 완료 및 Fault를 공정 이벤트로 기록한다.
- UI에는 자동 분류 활성 여부와 현재 단계를 표시하되 실제 제어 생명주기는 UI에 종속시키지 않는다.

### 5단계: 설정값 분리

다음 값은 상수로 흩어놓지 않고 설정 또는 하나의 정책 객체로 관리한다.

- 센서 폴링 주기
- Sorter 도착 제한시간
- Sorter 이탈 제한시간
- 필요 시 출력 선행/후행 지연시간
- 자동 분류 활성 여부

## 11. 검증 계획

### Mock 검증

- Blue Lid 센서 상승 → Sorter 도착 → 센서 해제 정상 흐름
- 카메라 신호 중복 발생 시 1회만 처리
- Green Lid 입력에서 Blue Lid 출력이 켜지지 않음
- Coil 7과 Coil 8 동시 ON 방지
- 도착 센서 시간초과
- 이탈 센서 고착
- 분류 중 통신 단절
- 자동 분류 중지 시 출력 OFF

### Factory I/O 통합 검증

1. Blue Lid 제품 1개를 낮은 속도로 투입한다.
2. Blue Lid Camera와 Write Sensor 발생 순서를 기록한다.
3. Read Sensor Sorter 1 도착 시 오른쪽 이송이 시작되는지 확인한다.
4. 제품 이탈 후 Coil 6~8이 요구된 안전 상태로 복귀하는지 확인한다.
5. Blue Lid 5개 연속 운전에서 누락·중복 분류가 없는지 확인한다.
6. Blue/Green Lid 교차 투입에서 Blue Lid만 목표 오른쪽 경로로 이동하는지 확인한다.
7. 센서 고착 및 네트워크 단절 시 출력이 안전하게 종료되는지 확인한다.

## 12. 완료 기준

- Factory I/O와 C# 주소표가 최종 I/O 계약과 일치한다.
- Blue Product Lid가 Sorter 1에서 오른쪽 경로로 정상 이송된다.
- Green Lid가 Blue Lid 경로로 오분류되지 않는다.
- 제품 한 개당 완료 이벤트가 정확히 한 번 기록된다.
- 정상 이탈 후 Sorter 출력이 OFF 또는 현장 검증된 대기 상태로 복귀한다.
- 센서 고착, 시간초과 및 통신 오류에서 Fault가 기록되고 위험 출력이 해제된다.
- Mock 검증과 실제 Factory I/O 반복 시험을 모두 통과한다.
- 최종 주소표, 출력 조합, 제한시간 및 시험 결과가 문서에 반영된다.

## 13. 구현 착수 조건

다음 항목이 모두 충족된 후 코드를 작성한다.

- [ ] Input 5~7과 Coil 6~8 매핑 완료
- [ ] Blue Lid 오른쪽 이송 출력 조합 확인
- [ ] Read Sensor Sorter 1 위치와 신호 동작 확인
- [ ] 오른쪽 수신 컨베이어 구동 조건 확인
- [ ] 센서 실제 발생 순서 확인
- [ ] Factory I/O 씬 별도 저장 및 최종 I/O 화면 캡처
- [ ] IP, Port, Slave ID 및 방화벽 확인
- [ ] 테스트 중 한 번에 한 제품만 Sorter 구간에 진입하도록 운전 조건 확보

## 14. 구현 후 문서 갱신 항목

실제 시험 결과에 따라 다음 항목을 이 문서에 최종 반영한다.

- 확정된 Sorter 출력 진리표
- 오른쪽 이송에 필요한 ON/OFF 순서와 지연시간
- 오른쪽 수신 컨베이어의 추가 주소 유무
- 센서 발생 순서
- 타임아웃 기준값
- 반복 시험 결과와 발견된 예외 조건
