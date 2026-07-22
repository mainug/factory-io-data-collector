# Factory I/O 가공 공정 트러블슈팅 기록

마지막 갱신: 2026-07-22

## 문서 목적

Factory I/O의 가공 도입부와 WinForms MES를 Modbus TCP로 연동하면서 확인한 문제, 원인, 수정 사항 및 검증 결과를 기록한다. 실제 씬 파일은 `C:\Users\Admin\Documents\Factory IO\My Scenes`에서 관리하며 저장소의 `.factoryio` 파일은 추적하지 않는다.

## 현재 Modbus 매핑

### Coil 출력

| 주소 | Factory I/O 태그 | 용도 |
|---|---|---|
| Coil 0 | Entrance belt | 원소재 입구 벨트 |
| Coil 1 | Machining Center (1 Lids 0 Base) | OFF=Base, ON=Lid |
| Coil 2 | Machining Center (Start) | 200ms 시작 펄스 |
| Coil 3 | Machining Center (Stop) | 200ms 정지 펄스 |
| Coil 4 | Machining Center (Reset) | 200ms 리셋 펄스 |
| Coil 5 | Exit Belt Sorter 1 | 가공품 출구 벨트 |

### Discrete Input

| 주소 | Factory I/O 태그 | 용도 |
|---|---|---|
| Input 0 | Stop Entrance Belt Sensor | 원소재 입구 감지 |
| Input 1 | Machining Center (Is Busy) | 가공 중 상태 및 Start 승인 확인 |
| Input 2 | Machining Center (Has Error) | 가공기 오류 상태 |
| Input 3 | Machining Center (Opened) | 가공기 개방 상태 및 사이클 전환 확인 |
| Input 4 | Write Sensor | 가공 완료품의 다음 공정 인계 감지 |

### Input Register

| 주소 | Factory I/O 태그 | 용도 |
|---|---|---|
| Input Register 0 | Machining Center (Progress) | 가공 진행률 |

## 현재 운전 조건

- Entrance Emitter는 Factory I/O에서 Forced 상태로 사용한다.
- 소재 생성 주기는 현재 약 40초로 설정한다.
- 입구 센서 감지 후 Entrance belt는 1,500ms 더 이동한 뒤 정지한다.
- 자동 가공 모드는 소재가 없어도 미리 활성화할 수 있다.
- Start, Stop, Reset은 유지 신호가 아니라 200ms 펄스로 출력한다.
- Reset은 자동 실행하지 않고 작업자가 상태를 확인한 뒤 실행한다.

## 발생 문제와 수정 이력

### 1. 생산 목표가 0으로 표시됨

현상: 작업지시 목표가 설정되어 있어도 대시보드 목표 생산량과 진행률이 0으로 표시됐다.

원인: 활성 작업지시가 선택되지 않은 상태였다.

조치: 작업지시 시작 시 활성 작업지시와 생산 기준 수량을 동기화했다.

### 2. 달성률 100% 이후 설비가 멈추지 않음

현상: 목표 수량 달성 후에도 생산 상태가 유지됐다.

조치: 작업지시별 기준 수량, 중복 완료 방지 및 목표 달성 시 작업 완료 동기화를 추가했다. 실제 설비의 자동 가동은 안전상 작업지시 시작과 분리했다.

### 3. 설비 화면 갱신 시 스크롤이 위로 이동함

현상: 주기적인 상태 갱신 때문에 하단 설비의 Start/Stop 버튼을 누르기 어려웠다.

원인: 갱신할 때마다 DataGridView의 DataSource가 재설정됐다.

조치: 데이터 바인딩 전후의 세로 행 위치와 가로 스크롤 위치를 보존했다.

### 4. 입구 센서 감지 후 벨트가 멈추지 않음

원인: 센서를 모니터링만 하고 Coil 0과 연동하지 않았다.

조치: 입구 센서 ON 시 Entrance belt를 자동 정지하고, 센서 점유 중 수동 재가동을 차단했다.

### 5. 입구 벨트가 너무 일찍 멈춤

현상: 소재가 로봇 픽업 위치까지 도달하지 못했다.

조치: 센서 감지 후 정지 지연을 700ms, 1,000ms로 시험한 뒤 현재 1,500ms로 설정했다.

### 6. 센서가 OFF되면 가공 시작 버튼이 비활성화됨

원인: 버튼이 입구 센서의 현재 ON 상태만 확인했다. 소재가 센서를 통과한 뒤에는 준비된 소재가 있어도 버튼이 비활성화됐다.

조치: 센서가 한 번 감지되면 `material ready` 상태를 유지하고 Busy가 Start를 승인한 뒤 해제하도록 변경했다.

### 7. 첫 Start만 동작하고 이후 자동 가공되지 않음

원인: Busy의 OFF→ON 상승 순간에만 Entrance belt 재가동을 처리하여 폴링 사이에 전환을 놓칠 수 있었다.

초기 조치: Start 요청 상태를 기억하고 Busy 확인 시 벨트를 재가동하도록 변경했다.

후속 조치: 다음 소재가 가공 중 미리 진입하는 문제를 줄이기 위해 Busy 직후 및 Opened ON 직후 재가동 방식을 모두 폐기했다. `Opened OFF → Opened ON`은 정상 사이클 확인에만 사용하며, 현재는 가공 사이클의 최종 `Busy OFF`를 확인한 다음 Entrance belt를 재가동한다.

### 8. 한 번씩 가공 Start가 무시되고 다음 소재가 와야 시작됨

현상: 입구 소재 한 개가 준비되어도 가공하지 않고, 다음 소재가 도착한 뒤 앞 소재가 가공됐다.

원인: Start 펄스가 가공기 준비 조건과 맞지 않아 무시됐지만 WinForms가 소재 준비 상태를 즉시 해제했다.

조치:

- Start 펄스 전송만으로 가공 시작을 확정하지 않는다.
- Busy ON을 실제 Start 승인으로 판단한다.
- 3초 안에 Busy가 켜지지 않으면 같은 소재에 Start 펄스를 재전송한다.
- Busy가 확인된 이후에만 현재 소재 준비 상태를 해제한다.
- Start 허용 조건에 `Busy OFF`, `Error OFF`, `Opened ON`, `material ready`를 적용한다.

### 9. 장시간 운전 후 입구에 소재가 두 개 쌓임

원인 후보:

- Forced Emitter의 생성 주기와 전체 사이클의 시간 편차가 작았다.
- Busy 직후 Entrance belt를 재가동해 다음 소재가 너무 일찍 진입했다.

조치:

- 소재 생성 주기를 현재 약 40초로 조정했다.
- Entrance belt 재가동 시점을 가공기의 최종 Busy OFF 뒤로 이동했다.
- Forced Emitter 방식에서는 WinForms가 소재 생성을 직접 차단할 수 없으므로 장시간 정지 시 누적 가능성이 남아 있다.

### 10. 가공품 누적과 Busy 유지

초기 판단: 출구 제품 누적으로 가공품 배출 위치가 막혔다고 판단했다.

수정된 판단: 현장 화면에서 실제 가공품 낙하 위치는 비어 있었으므로 하류 누적이 Machining Center를 직접 막았다고 단정할 수 없다. 하류 병목과 가공기 상태 정체를 별도 문제로 추적한다.

현재 조치:

- Exit Belt Sorter 1을 Coil 5로 추가했다.
- Write Sensor를 Input 4로 모니터링한다.
- 가공기의 Busy, Error, Opened ON/OFF 전환과 모든 Coil 명령을 로그에 기록한다.

## 현재 자동 가공 상태 흐름

```text
자동 모드 ON
  → 소재 감지
  → Entrance belt 1,500ms 후 정지
  → Busy OFF / Error OFF / Opened ON 확인
  → Start Coil 2를 200ms 펄스 출력
  → Busy ON 확인
     ├─ 3초 안에 Busy ON: Start 승인
     └─ Busy 미응답: Start 펄스 재시도
  → Opened OFF 확인
  → Opened ON 재확인
  → Busy OFF 최종 확인
  → 정상 사이클이면 Entrance belt 재가동
  → 다음 소재 대기
```

Reset 복구 경로에서는 위 정상 흐름과 달리 Entrance belt를 즉시 재가동하지 않는다. Reset 즉시 벨트를 끄고, Busy OFF 후 5초 안정화가 끝난 다음 작업자가 자동 가공 모드를 다시 시작한다.

## 로그 수집

상세 로그 파일:

```text
MesProj\bin\Debug\MesProj.log
```

현재 기록 항목:

- `MODBUS WRITE`: 사용자가 실행하거나 자동 가공에서 출력한 Coil ON/OFF
- `MODBUS INPUT`: 센서의 ON/OFF 전환
- `AUTO WRITE`: Entrance belt 자동 정지 및 재가동
- 통신 연결 실패와 명령 예외

공정 이벤트 파일:

```text
MesProj\bin\Debug\Data\process_events_yyyyMMdd.csv
```

이벤트 파일은 현재 주로 센서 상승 이벤트를 기록한다. 상세한 OFF 전환 및 명령 시각 분석에는 `MesProj.log`를 사용한다.

## 장시간 운전 검증 절차

1. WinForms를 완전히 종료한 뒤 새 빌드로 실행한다.
2. Factory I/O에 연결한다.
3. Exit Belt Sorter 1을 필요한 상태로 가동한다.
4. 자동 가공 모드를 시작한다.
5. 최소 10개 이상의 소재를 연속 처리한다.
6. 정체가 발생하면 Stop 또는 Reset을 누르기 전에 화면을 캡처한다.
7. Busy, Error, Opened, Progress, 입구 센서 및 Write Sensor 상태를 기록한다.
8. `MesProj.log`의 정체 직전 구간을 분석한다.

## 다음 작업 후보

- Write Sensor ON→OFF를 제품 1개 통과로 판정
- 가공 시작 시 선택한 Base/Lid 정보와 Write Sensor 통과 이벤트 연결
- Opened 이후 Write Sensor 미도달 타임아웃 알람
- Write Sensor 장시간 ON 시 출구 정체 알람
- 자동 가공 모드와 Exit Belt 자동 운전 연동
- 카메라 판별 및 Blue/Green, Base/Lid 분류 구간 제어
- 통신 설정 화면에서 Entrance belt 정지 지연시간을 조정할 수 있도록 설정화

## Busy 고착 Reset 복구 확인

고착 상태에서 Stop 펄스를 두 차례 전송했지만 Busy는 해제되지 않았다. Reset 펄스 전송 후 로봇이 소재를 꺼내 배출하고 Busy가 OFF됐다. 일부 시험에서는 Write Sensor가 ON→OFF됐지만 다른 시험에서는 Write Sensor 반응 없이 Busy만 OFF됐다. 따라서 Reset은 단순 오류 비트 초기화가 아니라 진행 중인 고착 사이클을 강제 배출 및 종료시키는 효과가 있으며, Write Sensor만으로 복구 완료를 판단해서는 안 된다.

자동 보호 로직:

- Opened ON 이후 Busy가 30초 이상 유지되면 `MACHINING_BUSY_TIMEOUT` Warning 알람 발생
- 타임아웃 발생 시 WinForms 자동 가공 모드 해제
- 작업자가 상태 확인 후 Reset 수행
- Reset 이후 Write Sensor가 반응하면 상승 이벤트를 `Aborted`로 기록
- Write Sensor가 반응하지 않는 복구도 있으므로 Busy OFF를 기본 복구 완료 신호로 사용
- Busy OFF 시 복구 완료를 상세 로그에 기록
- Reset 신호를 전송하는 즉시 Entrance belt를 OFF하여 복구 중 신규 소재 유입을 차단한다.
- Reset 이후 Busy OFF가 되어도 로봇 배출 및 복귀 시간을 고려해 5초 동안 안정화한 뒤 복구 완료로 처리한다.
- Reset은 자동 가공 모드를 해제하며, 작업자가 다시 `자동 가공 시작`을 누르면 입구가 비어 있는 경우 Entrance belt를 자동 재가동한다.
- 복구 뒤 자동 가공은 작업자가 직접 다시 시작

확정된 작업자 복구 순서:

1. `MACHINING_BUSY_TIMEOUT` 알람과 실제 설비 상태를 확인한다.
2. 필요하면 Stop을 누르되, Stop만으로 Busy가 해제되지 않을 수 있음을 인지한다.
3. Reset을 누른다. 이때 Entrance belt는 즉시 OFF되어 신규 소재 유입이 차단된다.
4. 로봇이 기존 소재를 배출하고 Busy가 OFF될 때까지 기다린다.
5. Busy OFF 후 5초 안정화가 완료될 때까지 기다린다.
6. 입구와 로봇 작업 영역에 소재가 겹치지 않았는지 확인한다.
7. `자동 가공 시작`을 다시 누른다. 입구가 비어 있으면 Entrance belt가 자동으로 ON된다.

## Has Error가 설비 Reset으로 해제되지 않는 문제

현상: 로봇이 소재를 정상 위치 밖으로 던진 뒤 `Machining Center (Has Error)`가 ON됐다. 튕겨 나간 소재를 제거하고 가공기 내부, 로봇 그리퍼 및 픽업 위치가 모두 비어 있는 것을 확인했지만 Has Error가 유지됐다.

확인 결과:

- WinForms의 오류 리셋은 Coil 4를 약 200ms 동안 정상적으로 ON→OFF했다.
- Factory I/O에서 `Machining Center (Reset)`을 직접 Forced로 약 1초 ON한 뒤 OFF해도 해제되지 않았다.
- 가공기 옆 현장 제어반의 Reset 버튼도 효과가 없었다.
- Busy는 OFF였지만 Has Error만 ON으로 래치된 상태였다.
- 물리적으로 걸리거나 가공기 내부에 남아 있는 소재는 없었다.

판단: Modbus 주소, WinForms 명령 및 Reset 펄스 길이의 문제가 아니라 Factory I/O Machining Center 내부 상태가 일반 설비 Reset으로 복구되지 않는 씬 수준의 고착 상태로 판단한다.

최종 조치: Factory I/O 상단 도구 모음의 원형 화살표 `씬 Reset`을 사용하여 전체 시뮬레이션을 초기화했다.

복구 단계 구분:

1. 일반 Busy 고착 또는 사이클 오류는 WinForms의 `오류 리셋`으로 1차 복구한다.
2. Has Error가 유지되면 소재 잔류와 로봇 작업 영역을 확인하고 현장 Reset을 시험한다.
3. Coil 4 Reset, 직접 Forced Reset 및 현장 Reset이 모두 실패하면 Reset을 반복하지 않는다.
4. 자동 운전을 중지하고 Factory I/O `씬 Reset`으로 단계 상승한다.
5. 씬 Reset 후 Has Error OFF, Busy OFF, Opened ON, Modbus 연결 및 Entrance Emitter 설정을 확인한다.
6. 생성 주기 약 40초와 Forced 상태가 유지되는지 확인한 뒤 자동 가공을 다시 시작한다.

주의: 씬 Reset은 설비 한 대가 아니라 전체 시뮬레이션 상태와 소재 위치에 영향을 줄 수 있으므로 최후 복구 수단으로 사용한다. Has Error 입력에 고장 주입 또는 강제 ON이 설정된 경우에는 씬 Reset 전에 해당 설정도 해제해야 한다.

## 화면 전환 시 자동 가공이 해제되는 문제

현상: 장시간 운전 중 데이터 분석 화면으로 이동하면 알람 없이 자동 가공 버튼이 `자동 가공 시작`으로 돌아가고 다음 소재에서 공정이 멈췄다.

원인: 자동 가공 상태와 감시 로직이 `EquipmentControlControl` 인스턴스에 저장되어 있었고, MainForm은 메뉴를 이동할 때 기존 화면을 Dispose했다. 데이터 분석 화면을 여는 순간 설비 제어 컨트롤이 폐기되어 자동 가공 상태도 함께 사라졌다.

조치: 설비 제어 컨트롤을 MainForm 수명 동안 하나의 인스턴스로 유지하고 화면 전환 시 Dispose하지 않도록 변경했다. 이제 대시보드, 데이터 분석, 알람 이력 등 다른 메뉴를 열어도 자동 가공과 Busy 타임아웃 감시가 계속 동작한다.

## 2026-07-22 장시간 운전 정체 분석

상세 로그에서 정상 사이클은 `Opened ON` 이후 약 13초가 지나야 Busy가 OFF되는 것으로 확인됐다. 따라서 Opened ON은 가공 전체 완료가 아니라 문 개방 단계이며, 이 시점에 Entrance belt를 재가동한 기존 로직은 다음 소재를 너무 일찍 로봇 픽업 구간으로 공급했다.

정체 시각의 마지막 사이클은 Start, Busy ON, Opened OFF, Opened ON까지 정상 진행했지만 Busy OFF가 발생하지 않았다. 오른쪽 하류의 누적 제품을 모두 제거해도 Busy 상태가 풀리지 않았으므로 하류 병목이 직접 원인이라는 가설은 제외했다.

조치 사항:

- Entrance belt 재가동 조건을 Opened ON에서 Busy OFF로 변경했다.
- 가공 중에는 다음 소재를 픽업 구간으로 보내지 않는다.
- 복구 테스트 전 로봇 입구에 겹친 소재를 제거하고 Stop 및 Reset을 수행한다.

## 가공 공정 확정 상태

2026-07-22 기준으로 가공 도입부부터 Machining Center 배출까지의 제어와 복구 절차를 검증했다. 이후 기능 개발에서는 재현 가능한 신규 문제가 발생하지 않는 한 이 구간의 로직을 변경하지 않는다.

확정 항목:

- Entrance Emitter Forced, 생성 주기 약 40초
- 입구 센서 감지 후 1,500ms 지연 정지
- Busy 응답 기반 Start 승인과 3초 미응답 재시도
- 최종 Busy OFF 이후에만 다음 소재 투입
- Opened ON 이후 Busy 30초 유지 시 고착 알람
- Reset 시 입구 벨트 즉시 차단
- Reset 복구 Busy OFF 후 5초 안정화
- 복구 후 작업자가 자동 가공 모드를 다시 시작
- 화면 전환 중에도 자동 운전 및 감시 상태 유지

다음 개발 범위는 Write Sensor 이후의 가공품 인계, Exit Belt Sorter 1 운전, 카메라 판별, 색상·형태 분류 및 적재 공정이다.

## 설비 제어 화면 공정별 분리

설비 제어 화면이 하나의 목록에 모든 I/O를 표시하면 공정이 확장될수록 조작 대상과 센서의 소속을 구분하기 어렵다. 통신 상태는 전체 공정이 공유하므로 화면 상단에 유지하고, 하단 제어 영역을 다음 탭으로 분리했다.

- `가공 공정`: 현재 검증이 끝난 원소재 투입, Machining Center 상태·명령 및 관련 센서
- `분류·적재 공정`: Write Sensor 이후 출구 이송, 카메라 판별, 색상·형태 분류 및 적재 설비를 추가할 영역

가공 공정의 제어 상태와 자동 운전 인스턴스는 기존처럼 MainForm 수명 동안 유지되므로 탭 또는 좌측 메뉴를 전환해도 해제되지 않는다.
