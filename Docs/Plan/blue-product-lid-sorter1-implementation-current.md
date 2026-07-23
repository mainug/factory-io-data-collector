# Blue Product Lid Sorter 1 구현 현황

- 최종 갱신일: 2026-07-23
- 문서 범위: Factory I/O 매핑·센서 시험, C# 자동 분류 구현, UI 분리, 수동 출력 및 안전 인터록 검증 현황
- 코드 구현 상태: 자동 제품 1개 이송 및 Blue Lid Belt 1 안착 검증 완료

## 1. 목표

Blue Raw Material을 가공하여 생성된 `Blue Product Lid`가 Sorter 1에 도착하면 오른쪽으로 분기하고, `Blue Lid Belt 1`이 제품을 인계받아 다음 구간으로 이송하도록 자동화한다.

## 2. 현재 Factory I/O Modbus 설정

- IP: `210.119.14.67`
- Port: `502`
- Slave ID: `1`
- 주소 기준: 0-based
- 드라이버: Modbus TCP/IP Server
- 서버 조작 상태: STOP 후 RESTART 완료

서버 재시작으로 기존 Modbus TCP 연결은 종료되었다. C# 자동 제어 시험 전 새 연결을 생성해야 하며, 재시작 후 I/O 매핑 유지 여부와 Force 해제 상태를 화면에서 최종 확인한다.

## 3. 확인된 I/O 매핑

### 입력

| 주소 | Factory I/O 태그 | 상태 |
|---|---|---|
| Input 0 | Stop Entrance Belt Sensor | 기존 매핑 유지 |
| Input 1 | Machining Center (Is Busy) | 기존 매핑 유지 |
| Input 2 | Machining Center (Has Error) | 기존 매핑 유지 |
| Input 3 | Machining Center (Opened) | 기존 매핑 유지 |
| Input 4 | Write Sensor | 기존 매핑 유지 |
| Input 5 | Read Sensor Sorter 1 | 추가 및 동작 확인 완료 |
| Input 6 | Blue Lid Camera | 추가 완료 |
| Input 7 | Green Lid Camera | 추가 완료 |
| Input Reg 0 | Machining Center (Progress) | 기존 매핑 유지 |

### 출력

| 주소 | Factory I/O 태그 | 상태 |
|---|---|---|
| Coil 0 | Entrance belt | 기존 매핑 유지 |
| Coil 1 | Machining Center (1 Lids 0 Base) | 기존 매핑 유지 |
| Coil 2 | Machining Center (Start) | 기존 매핑 유지 |
| Coil 3 | Machining Center (Stop) | 기존 매핑 유지 |
| Coil 4 | Machining Center (Reset) | 기존 매핑 유지 |
| Coil 5 | Exit Belt Sorter 1 | 기존 매핑 유지 |
| Coil 6 | Sorter 1 Forward and Power | 추가 및 동작 확인 완료 |
| Coil 7 | Sorter 1 Blue Lid | 추가 및 동작 확인 완료 |
| Coil 8 | Sorter 1 Green Lid | 추가 완료, OFF 상태 확인 |
| Coil 9 | Blue Lid Belt 1 | 추가 및 연계 이송 확인 완료 |

## 4. 액추에이터 단독 및 조합 시험 결과

### 시험 A: Coil 6 단독 ON

```text
Coil 6 = ON
Coil 7 = OFF
Coil 8 = OFF
```

- 제품 이동에 가속도가 붙는다.
- Sorter 롤러의 방향은 바뀌지 않는다.
- 제품은 기존 진행 방향으로 직진한다.
- 결론: Coil 6은 Sorter 1의 전진 방향과 구동력을 활성화한다.

### 시험 B: Coil 7 단독 ON

```text
Coil 6 = OFF
Coil 7 = ON
Coil 8 = OFF
```

- Sorter 롤러 방향은 Blue Lid 경로 쪽으로 전환된다.
- Coil 6이 OFF이므로 제품은 이동하지 않는다.
- 결론: Coil 7은 동력을 공급하지 않고 Blue Lid 분기 방향만 선택한다.

### 시험 C: Coil 6과 Coil 7 동시 ON

```text
Coil 6 = ON
Coil 7 = ON
Coil 8 = OFF
```

- 제품이 오른쪽 대각선 방향으로 이동한다.
- Coil 6의 직진 구동 성분과 Coil 7의 방향 전환이 결합된 결과다.
- 결론: Blue Lid를 Sorter 오른쪽으로 배출하려면 Coil 6과 Coil 7이 모두 필요하다.

### 시험 D: Coil 6, Coil 7, Coil 9 연계 ON

```text
Coil 6 = ON
Coil 7 = ON
Coil 8 = OFF
Coil 9 = ON
```

- 제품이 Sorter 1에서 오른쪽 대각선으로 분기한다.
- 제품이 `Blue Lid Belt 1`에 정상 안착한다.
- 안착 후 벨트를 따라 막힘 없이 다음 구간으로 이동한다.
- `Sorter 1 Green Lid`는 OFF 상태로 유지되었다.
- 결론: Blue Lid 오른쪽 분기 및 수신 벨트 연계에 필요한 출력 조합이 확인되었다.

## 5. 센서 발생 순서 및 유지시간 시험 결과

시험 영상은 FFmpeg로 프레임 단위 재확인했다. 영상의 절대 또는 상대 재생 시각은 제어 기준으로 사용하지 않고 센서 에지 순서와 ON 유지시간만 사용한다.

확인된 전체 에지 순서는 다음과 같다.

```text
1. Write Sensor ON
2. Blue Lid Camera ON
3. Write Sensor OFF
4. Blue Lid Camera OFF
5. Read Sensor Sorter 1 ON
6. Read Sensor Sorter 1 OFF
```

| 센서 | ON 유지시간 | 확인 결과 |
|---|---:|---|
| Write Sensor | 약 0.6초 | 제품당 1회 ON/OFF |
| Blue Lid Camera | 약 1.2초 | 제품당 1회 ON/OFF |
| Read Sensor Sorter 1 | 약 0.6초 | 도착 시 ON, 이탈 후 OFF |

- Write Sensor와 Blue Lid Camera는 짧은 시간 동시에 ON된다.
- Blue Lid Camera가 OFF된 후 Read Sensor Sorter 1이 ON된다.
- 관찰된 신호 깜빡임이나 중복 감지는 없다.
- 절대 시간 지연에 의존하지 않고 상승·하강 에지와 제품 상태 래치를 사용한다.

가장 짧은 센서 신호가 약 0.6초이므로 현재 C# 코드의 1초 폴링을 그대로 사용하면 신호를 놓칠 수 있다. 구현 시 관련 센서 폴링 주기는 `50~100ms` 범위로 설정해야 한다.

## 6. 현재까지 확정된 제어 원칙

Sorter 1은 각도 숫자 `90`을 입력하는 장치가 아니다. Boolean 출력 조합으로 롤러 방향과 동력을 제어한다.

- Coil 6: Sorter 전진 및 동력
- Coil 7: Blue Lid 방향 선택
- Coil 8: Green Lid 방향 선택
- Coil 9: Blue Lid Belt 1 구동
- Coil 7과 Coil 8은 동시에 ON하지 않는다.
- Blue Lid 분기에는 Coil 6과 Coil 7이 모두 필요하다.
- 제품을 안정적으로 받기 위해 Coil 9를 Sorter 배출 전에 먼저 구동한다.

## 7. C#에 구현된 자동 제어 순서

강제 출력 및 센서 순서 시험 결과를 바탕으로 다음 상태 흐름을 구현했다. 자동 분류는 Modbus 연결만으로 시작되지 않으며, `분류·적재 공정` 탭에서 작업자가 `자동 분류 시작`을 눌러야 활성화된다.

```text
1. 자동 분류 시작 시 Coil 5 ON
2. Blue Product Lid 판정
3. Coil 7 ON       // Blue Lid 방향 선택
4. Coil 9 ON       // Blue Lid Belt 1 수신 준비
5. Read Sensor Sorter 1 ON 확인
6. Coil 6 ON       // Sorter 동력 공급 및 오른쪽 분기
7. Read Sensor Sorter 1 OFF 확인
8. Coil 6·7·9를 1.5초 추가 유지
9. Coil 6·7 OFF
10. 제품 안착을 위해 Coil 9를 750ms 추가 유지
11. Coil 9 OFF
12. 제품 추적 상태 초기화
13. 자동 분류 중지 시 Coil 5~9 OFF
```

현재 구현값:

- Sorter 센서 폴링 주기: `50~100ms`
- Blue Lid 판별 대기 제한: `3초`
- Sorter 도착 대기 제한: `10초`
- Sorter 이탈 대기 제한: `5초`
- Input 5 OFF 후 Coil 6·7·9 배출 유지: 설정값 `1.5초`
- Input 5 OFF 후 Coil 9 추가 유지: `750ms`

시간초과 시 관련 출력을 안전 OFF하고 알람을 발생시킨다. 위 시간값은 자동 제품 이송 시험 후 필요하면 조정한다.

### 자동 제품 이송 시험 결과

첫 번째 자동 시험에서는 Input 5가 OFF되자마자 Coil 6과 Coil 7을 OFF하여 제품이 Sorter 입구 부근에서 멈췄다. 로그상 Coil 6의 구동시간은 약 `0.625초`에 불과했다. Input 5 OFF는 제품의 Sorter 통과 완료가 아니라 제품 뒤쪽이 입구 센서를 벗어난 시점으로 판정해야 한다.

이를 반영하여 `ClearingSorter` 상태를 추가하고 Input 5 OFF 이후 Coil 6·7·9를 설정상 `1.5초` 추가 유지하도록 수정했다. 수정 후 두 번째 시험 결과는 다음과 같다.

```text
11:39:48.599  Blue Lid Camera ON
11:39:48.606  Coil 7 ON
11:39:48.609  Coil 9 ON
11:39:52.876  Read Sensor Sorter 1 ON
11:39:52.879  Coil 6 ON
11:39:53.513  Read Sensor Sorter 1 OFF
11:39:55.656  Coil 6 OFF
11:39:55.658  Coil 7 OFF
11:39:56.428  Coil 9 OFF
```

- 제품이 Sorter 1에서 Blue Lid 방향으로 정상 분기했다.
- 제품이 `Blue Lid Belt 1`에 정상 진입·안착했다.
- 폴링 및 Modbus 처리시간을 포함한 실제 Coil 6·7 추가 유지시간은 약 `2.14초`였다.
- Coil 6·7 OFF 후 Coil 9 실제 후행 유지시간은 약 `0.77초`였다.
- 시험 종료 시 자동 분류 중지로 Coil 5~9가 모두 OFF됐다.
- 상류 자동 가공이 계속 실행되어 후속 제품이 유입됐으므로, 단일 제품 시험 시 자동 가공과 Entrance Belt를 별도로 정지해야 한다.

## 8. 완료된 작업

- [x] 기존 Input 0~4 및 Coil 0~5 매핑 유지
- [x] Input 5에 Read Sensor Sorter 1 매핑
- [x] Input 6에 Blue Lid Camera 매핑
- [x] Input 7에 Green Lid Camera 매핑
- [x] Coil 6에 Sorter 1 Forward and Power 매핑
- [x] Coil 7에 Sorter 1 Blue Lid 매핑
- [x] Coil 8에 Sorter 1 Green Lid 매핑
- [x] Coil 9에 Blue Lid Belt 1 매핑
- [x] Coil 6 단독 직진 구동 확인
- [x] Coil 7 단독 방향 전환 확인
- [x] Coil 6과 Coil 7의 오른쪽 대각선 이송 확인
- [x] Coil 6, Coil 7, Coil 9 연계 이송 확인
- [x] Blue Lid Belt 1 정상 안착 확인
- [x] Read Sensor Sorter 1 도착 시 ON 확인
- [x] Read Sensor Sorter 1 이탈 후 OFF 확인
- [x] Write Sensor → Blue Lid Camera → Read Sensor Sorter 1 발생 순서 확인
- [x] Write Sensor, Blue Lid Camera 및 Read Sensor Sorter 1 유지시간 확인
- [x] Modbus TCP/IP Server STOP 후 RESTART 완료
- [x] 서버 재시작 후 Modbus TCP/IP Server `Started` 확인
- [x] I/O 매핑 유지 및 Coil 6~9 Force 해제 확인
- [x] C# `FactoryIoMap`에 Input 5~7과 Coil 6~9 정의 추가
- [x] 실제 Modbus 서비스의 허용 설비 목록과 폴링 대상 확장
- [x] 폴링 주기를 `50~100ms` 범위로 제한
- [x] 센서 상승·하강 에지 기반 Blue Lid 상태 머신 구현
- [x] Coil 7과 Coil 8 동시 ON 방지 인터록 구현
- [x] 연결 및 연결 해제 시 Coil 5~9 안전 OFF 구현
- [x] C#에서 Coil 9 단독 ON/OFF 및 Blue Lid Belt 1 작동 확인
- [x] C#에서 Coil 7 단독 방향 전환 확인
- [x] C#에서 Coil 6 단독 전진 구동 및 OFF 정지 확인
- [x] C#에서 Coil 6·7·9 무부하 연계 구동 확인
- [x] Coil 7 ON 상태에서 Coil 8 ON 차단 확인
- [x] Coil 8 ON 상태에서 Coil 7 ON 차단 확인
- [x] 통신 종료 시 Coil 5~9 안전 해제 확인
- [x] 설비 제어 화면의 `가공 공정`과 `분류·적재 공정` I/O 표시 분리
- [x] `분류·적재 공정` 탭에 자동 분류 시작/중지 기능 구현
- [x] 연결 직후 자동 분류 기본 중지 처리
- [x] 자동 분류 시작 시 Coil 5 자동 ON 및 중지 시 Coil 5~9 안전 OFF 구현
- [x] 새 UI에서 연결 후 자동 분류 시작/중지 버튼 활성 상태 확인
- [x] 자동 분류 중지 버튼으로 Coil 5~9 안전 OFF 확인
- [x] 첫 자동 제품 시험 실패 원인 분석: Input 5 OFF 직후 조기 정지
- [x] Input 5 OFF 이후 Coil 6·7·9 배출 유지 상태 구현
- [x] 제품 1개 C# 자동 분류 및 Blue Lid Belt 1 정상 안착 확인
- [x] 자동 운전에서 센서 및 Coil 출력 순서 로그 검증
- [x] 시작 화면의 통신 모드 상태 문구를 실제 설정에 맞게 표시하도록 수정
- [x] Release 구성 빌드 성공

## 9. 아직 완료되지 않은 작업

- [ ] Mock 서비스에 동일한 태그 및 시험 시나리오 추가
- [ ] 자동 운전에서 Sorter 도착·이탈 제한시간 검증
- [ ] 반복 시험으로 Sorter 배출 유지시간과 Coil 9 유지시간 최종 확정
- [ ] 상류 자동 가공 중지와 단일 제품 투입 절차 정리
- [ ] Blue/Green Lid 교차 투입 검증
- [ ] 반복 운전 및 장애 상황 검증

## 10. 현재 판정

Factory I/O 측 필수 I/O 매핑, Sorter 1의 출력별 역할, Blue Lid 오른쪽 분기, Blue Lid Belt 1 인계, 세 센서의 발생 순서와 유지시간을 확인했다. C#에서는 I/O 정의, 빠른 폴링, 센서 에지 기반 상태 머신, Blue/Green 동시 출력 방지, 연결 해제 안전 정지 및 자동 분류 시작/중지 기능까지 구현했다.

현재 단계는 **C# 자동 분류 구현과 제품 1개 Blue Lid Belt 1 안착 검증 완료, 반복·교차 투입 및 장애 시나리오 시험 대기** 상태다. Mock 서비스는 인터페이스 호환만 반영됐으며 센서 시나리오 기반 상태 머신 검증은 아직 완료되지 않았다.

## 11. 다음 작업 순서

### Mock 검증

1. Mock 서비스에 Input 4~7과 Coil 5~9 상태를 반영한다.
2. 정상 흐름, Green Lid 취소, 센서 누락, 센서 고착 및 시간초과 시나리오를 추가한다.
3. 각 시나리오에서 Coil 5~9의 최종 OFF를 확인한다.

### 검증

1. 상류 자동 가공과 Entrance Belt를 제어하여 제품을 정확히 1개씩 공급한다.
2. Blue Lid 반복 운전으로 Sorter 배출 유지시간과 Coil 9 후행시간의 안정성을 확인한다.
3. 센서 누락·고착 및 도착·이탈 시간초과 시 Coil 5~9 안전 OFF와 알람을 검증한다.
4. Blue/Green Lid 교차 투입을 검증한다.
