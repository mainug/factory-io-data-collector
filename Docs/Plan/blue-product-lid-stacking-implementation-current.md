# Blue Product Lid 후속 이송 및 적재 구현 계획

- 작성일: 2026-07-23
- 선행 공정: `Blue Product Lid Sorter 1 자동 분류`
- 선행 문서: `blue-product-lid-sorter1-implementation-plan.md`
- 구현 상태: Factory I/O 장치 역할 및 I/O 계약 확인 전

## 1. 목적

Sorter 1에서 Blue Lid 경로로 분류된 `Blue Product Lid`를 Blue Lid 전용 후속 컨베이어로 이송하고, Pick & Place 또는 Blue Lid 전용 위치제어 장치를 이용하여 준비된 적재 박스에 안전하게 적재한다.

이 문서는 코드 구현 전에 다음 항목을 확정하기 위한 계획이다.

- Sorter 1 이후 Blue Lid의 실제 이동 경로
- 후속 Conveyor, Roller, Positioner 및 Pick & Place의 역할
- 센서와 액추에이터의 Modbus I/O 계약
- 제품 1개를 이송·픽업·적재하는 상태 순서
- 장치 충돌과 제품 유실을 방지하는 인터록
- Mock 및 실제 Factory I/O 검증 절차

## 2. 공정 경계

### 시작 조건

- 선행 Sorter 1 공정에서 Blue Lid 분류 완료
- 제품이 `Blue Lid Belt 1`을 끝까지 통과하여 후속 구간에 진입
- 선행 공정의 Coil 6·7이 OFF

### 종료 조건

- Blue Product Lid가 준비된 Blue Lid Box 안에 정상 투입
- Gripper 또는 Grab이 제품을 놓은 상태
- Pick & Place 또는 위치제어 장치가 안전한 대기 위치로 복귀
- 적재 수량이 1 증가
- 다음 제품을 받을 수 있는 상태로 초기화

### 포함

- Blue Lid Belt 1 이후 후속 이송
- Blue Lid 전용 Roller Conveyor 제어
- Stop Roller 위치 정지
- 적재 박스 위치 및 Clamp 확인
- Blue Lid 픽업, 이동 및 박스 투입
- 제품 1개 단위 상태 추적
- 시간초과와 통신 해제 시 안전 정지
- 공정 이벤트 및 알람 기록

### 제외

- Sorter 1 이전 가공 및 분류 로직
- Green Lid 및 Base 제품 적재
- 가득 찬 박스의 Stacker Crane 이송
- 복수 제품의 동시 픽업
- Factory I/O 씬의 기계 구조 변경
- PLC 프로그램 작성

## 3. 현재 화면에서 추정되는 이동 경로

```text
Blue Lid Belt 1
→ Blue Lid 후속 Roller Conveyor
→ Blue Lid Stop Roller Sensor
→ Pick & Place 또는 Blue Lid Grab 픽업 위치
→ Blue Lid Positioner에 고정된 Box
→ Box 내부 적재
```

현재 화면만으로는 다음 항목을 확정할 수 없다.

- `Blue Lid Belt 2`와 `Blue Lid Roller 1~5`의 정확한 구간 순서
- `Two-Axis Pick & Place 4`와 `Blue Lid X/Z/Grab` 장치가 동일 설비인지 별도 설비인지
- Rotate CW/CCW 중 제품 위치와 박스 위치로 이동하는 방향
- Gripper CW/CCW 중 파지와 해제를 수행하는 방향
- Blue Lid X/Z Set Point에 필요한 실제 좌표값
- 적재 완료를 직접 감지하는 센서의 존재 여부
- Box 최대 적재 수량과 적재 층별 좌표

위 항목은 코드 작성 전에 Factory I/O 단독 시험으로 확인한다.

## 4. 후보 I/O 목록

아래 주소는 아직 확정하지 않는다. 현재 사용 중인 Input 0~7, Coil 0~9 및 Input Register 0과 충돌하지 않도록 Factory I/O Modbus 드라이버에서 새 주소를 할당한 후 최종 계약표를 작성한다.

### Boolean 액추에이터 후보

| Factory I/O 태그                    | 예상 역할                   | 확인 필요 사항            |
| ----------------------------------- | --------------------------- | ------------------------- |
| Blue Lid Belt 2                     | 후속 Belt 구동              | 실제 경로 및 정방향       |
| Blue Lid Roller 1~5                 | Roller Conveyor 구간별 구동 | 각 Roller가 담당하는 구간 |
| Blue Lid Positioner (Clamp)         | 적재 Box 위치 고정          | ON이 Clamp인지 확인       |
| Blue Lid Grab                       | Blue Lid 파지               | ON이 파지인지 확인        |
| Two-Axis Pick & Place 4 Rotate CW   | 회전축 한 방향 이동         | 이동 목적지               |
| Two-Axis Pick & Place 4 Rotate CCW  | 회전축 반대 방향 이동       | 이동 목적지               |
| Two-Axis Pick & Place 4 Gripper CW  | Gripper 한 방향 회전        | 파지 또는 해제            |
| Two-Axis Pick & Place 4 Gripper CCW | Gripper 반대 방향 회전      | 파지 또는 해제            |

### Float/Register 액추에이터 후보

| Factory I/O 태그     | 예상 역할             | Modbus 후보      |
| -------------------- | --------------------- | ---------------- |
| Blue Lid X Set Point | 픽업·적재 X 목표 위치 | Holding Register |
| Blue Lid Z Set Point | 픽업·적재 Z 목표 위치 | Holding Register |

Float 값은 Coil에 매핑하지 않는다. Factory I/O Modbus 드라이버가 Float 값을 몇 개의 16-bit Register로 제공하는지와 Word 순서를 먼저 확인한다.

### Boolean 센서 후보

| Factory I/O 태그                           | 예상 역할                      |
| ------------------------------------------ | ------------------------------ |
| Blue Lid Conv Roller Sensor                | 후속 Roller 진입 감지          |
| Blue Lid Stop Roller Sensor                | Pick-up 정지 위치 도착 감지    |
| Blue Lid Positioner Sensor                 | 적재 Box 또는 Pallet 도착 감지 |
| Blue Lid Positioner (Clamp Sensor)         | Box Clamp 완료 확인            |
| Blue Lid Grab Sensor                       | 제품 파지 확인                 |
| Blue Lid X Moving Sensor                   | X축 이동 중 상태               |
| Blue Lid Z Moving Sensor                   | Z축 이동 중 상태               |
| Two-Axis Pick & Place 4 (Rotating)         | 회전축 이동 중 상태            |
| Two-Axis Pick & Place 4 (Gripper Rotating) | Gripper 이동 중 상태           |

### Float/Register 센서 후보

| Factory I/O 태그           | 예상 역할   | Modbus 후보    |
| -------------------------- | ----------- | -------------- |
| Blue Lid X Position Sensor | 현재 X 위치 | Input Register |
| Blue Lid Z Position Sensor | 현재 Z 위치 | Input Register |

## 5. 예상 자동 운전 시나리오

이 시나리오는 장치 역할을 확인하기 위한 초안이며, Factory I/O 단독 시험 후 확정한다.

### 5.1 적재 Box 준비

```text
Blue Lid Positioner Sensor ON
→ Blue Lid Positioner (Clamp) ON
→ Clamp Sensor ON 확인
→ BoxReady = true
```

- Positioner Sensor가 OFF이면 Clamp를 시작하지 않는다.
- 제한시간 안에 Clamp Sensor가 ON되지 않으면 알람을 발생시키고 적재를 금지한다.
- BoxReady가 false이면 Blue Lid를 픽업 위치로 추가 투입하지 않는다.

### 5.2 Blue Lid 후속 이송

```text
선행 공정에서 Blue Lid Belt 1 횡단 완료
→ 후속 Belt/Roller ON
→ Blue Lid Conv Roller Sensor ON
→ 제품 추적 상태 생성
→ Blue Lid Stop Roller Sensor ON
→ 후속 Belt/Roller OFF
→ LidAtPickup = true
```

- Stop Roller Sensor가 ON되기 전에는 Pick & Place를 움직이지 않는다.
- LidAtPickup 상태에서 다음 제품이 접근하면 상류 Belt/Roller를 정지한다.
- Stop Roller Sensor가 제한시간 내 ON되지 않으면 이송 시간초과 알람을 발생시킨다.

### 5.3 Blue Lid 픽업

```text
BoxReady = true
AND LidAtPickup = true
→ Pick & Place를 제품 위치로 이동
→ 이동 완료 확인
→ Gripper 또는 Grab 파지
→ Blue Lid Grab Sensor ON 확인
→ ProductGrabbed = true
```

- Grab Sensor가 ON되지 않으면 이동을 중지하고 재시도 또는 알람 상태로 전환한다.
- 제품을 잡지 못한 상태에서 박스 방향으로 이동하지 않는다.

### 5.4 Box 방향 이동 및 적재

```text
ProductGrabbed = true
→ Pick & Place를 Box 위치로 이동
→ 이동 완료 확인
→ Gripper 또는 Grab 해제
→ Blue Lid Grab Sensor OFF 확인
→ 적재 수량 +1
→ ProductLoaded = true
```

- Box Clamp Sensor가 중간에 OFF되면 즉시 동작을 중지한다.
- 제품 해제 후에도 Grab Sensor가 ON이면 적재 완료로 처리하지 않는다.

### 5.5 원점 복귀 및 다음 제품 준비

```text
ProductLoaded = true
→ Pick & Place 안전 대기 위치 복귀
→ 이동 완료 확인
→ LidAtPickup = false
→ ProductGrabbed = false
→ ProductLoaded = false
→ 다음 Blue Lid 수신 허용
```

## 6. 예상 상태 머신

```text
Disabled
→ Idle
→ WaitingForBox
→ WaitingForLid
→ LidAtStopper
→ MovingToPickup
→ Gripping
→ MovingToBox
→ Releasing
→ ReturningHome
→ WaitingForLid
```

| 상태           | 진입 조건      | 완료 조건             | 시간초과 시 조치   |
| -------------- | -------------- | --------------------- | ------------------ |
| Disabled       | 자동 적재 중지 | 작업자 시작           | 모든 관련 출력 OFF |
| WaitingForBox  | 자동 적재 시작 | Clamp Sensor ON       | Box 준비 알람      |
| WaitingForLid  | BoxReady       | Stop Roller Sensor ON | Lid 도착 알람      |
| LidAtStopper   | 제품 도착      | 픽업 이동 시작        | 상류 Roller OFF    |
| MovingToPickup | 제품·Box 준비  | 픽업 위치 도착        | 이동 알람          |
| Gripping       | 픽업 위치 도착 | Grab Sensor ON        | 파지 알람          |
| MovingToBox    | 제품 파지 완료 | Box 위치 도착         | 이동 알람          |
| Releasing      | Box 위치 도착  | Grab Sensor OFF       | 해제 알람          |
| ReturningHome  | 적재 완료      | 대기 위치 도착        | 복귀 알람          |

## 7. 필수 인터록

- Rotate CW와 Rotate CCW를 동시에 ON하지 않는다.
- Gripper CW와 Gripper CCW를 동시에 ON하지 않는다.
- Blue Lid X축과 Z축의 허용 이동 범위를 제한한다.
- Z축이 안전 높이에 있지 않으면 X축 장거리 이동을 금지한다.
- Box Positioner Sensor가 OFF이면 Clamp와 적재를 시작하지 않는다.
- Clamp Sensor가 OFF이면 제품을 Box 위치로 이동하지 않는다.
- Stop Roller Sensor가 OFF이면 픽업을 시작하지 않는다.
- Grab Sensor가 OFF이면 제품을 잡은 것으로 처리하지 않는다.
- 제품을 잡고 있는 동안 후속 제품의 Stop 위치 진입을 차단한다.
- Stop 위치가 점유된 동안 후속 Belt/Roller를 정지한다.
- 자동 적재 중지, 통신 해제 또는 시간초과 시 관련 출력을 안전 OFF한다.
- Float Set Point는 검증된 범위 밖의 값을 쓰지 않는다.
- 재연결 후 자동 적재를 자동 재개하지 않는다.

## 8. 코드 구현 전 Factory I/O 작업자 체크리스트

### 8.1 장치 소유 관계 확인

- [ ] Blue Lid Belt 2의 실제 이동 구간 확인
- [ ] Blue Lid Roller 1~5의 실제 구간과 정방향 확인
- [ ] Two-Axis Pick & Place 4와 Blue Lid X/Z/Grab의 관계 확인
- [ ] Pick & Place가 Blue Lid를 집는 실제 위치 확인
- [ ] Blue Lid Box의 적재 위치 확인

### 8.2 센서 단독 확인

- [ ] Conv Roller Sensor의 ON/OFF 위치 확인
- [ ] Stop Roller Sensor의 ON/OFF 위치 확인
- [ ] Positioner Sensor의 감지 대상 확인
- [ ] Clamp Sensor의 ON 조건 확인
- [ ] Grab Sensor의 ON 조건 확인
- [ ] X/Z Moving Sensor의 ON 조건 확인
- [ ] X/Z Position Sensor의 값과 단위 확인

### 8.3 액추에이터 단독 확인

- [ ] Belt 2 및 Roller 1~5를 제품 없이 각각 짧게 ON/OFF
- [ ] Positioner Clamp ON/OFF와 Clamp Sensor 관계 확인
- [ ] Rotate CW의 이동 목적지 확인
- [ ] Rotate CCW의 이동 목적지 확인
- [ ] Gripper CW의 파지·해제 방향 확인
- [ ] Gripper CCW의 파지·해제 방향 확인
- [ ] Blue Lid Grab ON/OFF의 실제 동작 확인
- [ ] X/Z Set Point의 안전한 최소·최대값 확인

### 8.4 제품 1개 강제 출력 시험

- [ ] Blue Lid 1개를 Stop Roller 위치까지 이송
- [ ] Roller 정지 후 제품이 안정적으로 위치하는지 확인
- [ ] 제품 위치로 이동
- [ ] 제품 파지 및 Grab Sensor ON 확인
- [ ] Box 위치로 이동
- [ ] 제품 해제 및 Grab Sensor OFF 확인
- [ ] Box 내부 정상 안착 확인
- [ ] Pick & Place 안전 위치 복귀 확인

### 8.5 Modbus 매핑

- [ ] 현재 Input 0~7, Coil 0~9, Input Register 0 유지
- [ ] 신규 Boolean Sensor를 빈 Input 주소에 매핑
- [ ] 신규 Boolean Actuator를 빈 Coil 주소에 매핑
- [ ] X/Z Set Point를 빈 Holding Register에 매핑
- [ ] X/Z Position Sensor를 빈 Input Register에 매핑
- [ ] Float Register Word 순서와 배율 확인
- [ ] 모든 신규 주소의 Force 해제 확인
- [ ] 최종 매핑 화면 캡처

## 9. C# 구현 계획

### 9.1 I/O 정의

- `FactoryIoMap`에 확정된 후속 이송·적재 I/O 추가
- Boolean과 Float/Register 정의 분리
- 수동 제어가 허용되는 출력 목록 제한
- 장치별 안전한 기본 OFF 상태 정의

### 9.2 공정 제어 분리

- Sorter 1 로직과 후속 적재 로직을 별도 상태로 관리
- `분류·적재 공정` 탭 안에서 다음 영역을 분리
  - Sorter 1 자동 분류
  - Blue Lid 후속 이송
  - Blue Lid 자동 적재
- 연결과 자동 적재 시작을 분리
- 자동 적재 시작·중지 버튼 제공

### 9.3 센서 처리

- Boolean Sensor를 `50~100ms` 간격으로 폴링
- 상승·하강 에지를 제품 1회 처리 기준으로 사용
- Moving Sensor와 Position Sensor를 함께 사용해 이동 완료 판정
- 짧은 신호를 놓치지 않도록 상태 래치 사용

### 9.4 상태 머신

- Box 준비 상태
- Blue Lid 도착 상태
- Pick-up 이동 상태
- 파지 상태
- Box 이동 상태
- 해제 상태
- 원점 복귀 상태
- 시간초과 및 오류 복구 상태

### 9.5 로그 및 알람

- Blue Lid 후속 구간 진입
- Stop Roller 도착
- Box Clamp 완료
- 파지 성공·실패
- Box 적재 완료
- 적재 수량 증가
- 각 이동 시간초과
- Clamp 상실
- Grab Sensor 불일치
- 통신 해제 안전 정지

## 10. 검증 시나리오

### 정상

- Blue Lid 1개 정상 이송·적재
- Blue Lid 3개 순차 적재
- 자동 적재 중지 후 안전 OFF
- 연결 해제 후 안전 OFF

### 센서 오류

- Conv Roller Sensor 누락
- Stop Roller Sensor 누락
- Stop Roller Sensor 고착
- Clamp Sensor 누락
- Grab Sensor 누락
- Grab Sensor 해제 실패
- Moving Sensor 고착

### 장치 오류

- Rotate 이동 시간초과
- Gripper 이동 시간초과
- X/Z 위치 미도달
- Box 없는 상태에서 제품 도착
- 제품을 잡은 상태에서 통신 해제

### 제품 흐름

- 다음 Blue Lid 조기 진입
- Stop 위치 제품 중복 진입
- Blue Lid와 다른 제품의 오투입
- Box 가득 참
- Box 교체 중 제품 도착

## 11. 완료 기준

- [ ] 최종 Modbus I/O 계약 확정
- [ ] 모든 액추에이터의 방향 및 역할 확인
- [ ] 모든 센서의 ON/OFF 조건 확인
- [ ] 제품 1개 강제 출력 적재 성공
- [ ] C# 자동 이송·적재 상태 머신 구현
- [ ] 시작·중지 및 연결 해제 안전 정지 검증
- [ ] Blue Lid 3개 연속 적재 성공
- [ ] 시간초과 및 센서 오류 시험 통과
- [ ] 공정 로그와 알람 기록 확인
- [ ] 구현 현황 문서 작성

## 12. 다음 작업

코드 작성 전에 Factory I/O에서 다음 세 가지를 우선 확인한다.

1. `Blue Lid Belt 2` 및 `Blue Lid Roller 1~5`의 실제 이동 구간과 순서
2. `Two-Axis Pick & Place 4`와 `Blue Lid X/Z/Grab`의 장치 관계
3. Stop Roller 위치에서 Blue Lid를 Box에 넣는 최소 강제 출력 순서

세 항목을 확인한 후 신규 I/O 주소를 확정하고 C# 구현을 시작한다.
