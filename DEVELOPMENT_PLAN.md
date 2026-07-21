# Factory I/O - WinForms - 데이터 분석 개발 방향

## 현재 진단

- WinForms 화면, 작업지시, 설비 제어, 알람, Mock 운전의 기본 구조는 이미 있다.
- 실제 `ModbusFactoryIoService`는 미구현이며 `FactoryIoMap` 주소도 임시값이다.
- 기존 생산실적/알람 저장소는 메모리 Mock이므로 앱 종료 시 데이터가 사라진다.
- 따라서 첫 통합 기준은 **Factory I/O 태그 계약 확정 → 안정적 수집 → 저장 → 분석** 순서다.

## 권장 구조

```text
Factory I/O (Modbus TCP Server)
  -> IFactoryIoService (연결/읽기/쓰기/재연결)
  -> 상태 스냅샷 + 이벤트
  -> CSV 수집(MVP), 이후 SQLite
  -> 분석 서비스(가동률/수율/생산속도/고장)
  -> WinForms 대시보드와 분석 화면
```

UI가 Modbus 주소를 직접 읽고 쓰지 않게 유지한다. 주소, 데이터 형식, 읽기/쓰기 권한은 하나의 태그 맵으로 관리한다.

## 단계별 구현

1. **MVP 통합**: Factory I/O 씬 확정, 드라이버를 Modbus TCP Server로 설정하고 태그명/주소/타입/권한 표를 작성한다.
2. **실통신**: NModbus 계열 라이브러리 확정, 묶음 읽기, timeout, 취소, 자동 재연결, 통신 품질 상태를 구현한다.
3. **데이터 모델**: 원시 시계열과 생산 이벤트를 분리한다. 원시값은 일정 주기, 제품 통과·불량·알람은 변화 시점에 저장한다.
4. **영속화**: 개발은 SQLite, 확장 시 PostgreSQL/SQL Server를 사용한다. CSV는 검증·내보내기 용도로 유지한다.
5. **분석**: 가동률, 수율, cycle time, 시간당 생산량, 설비별 fault duration을 먼저 구현한다. OEE는 계획정지와 이상속도 기준이 정의된 뒤 추가한다.

## 이번 MVP에 반영한 것

- 상태 이벤트를 날짜별 UTF-8 CSV로 저장한다.
- 최근 3,600개 샘플로 가동률, 수율, 분당 생산량, 고장 감지 횟수를 계산한다.
- WinForms 메뉴에 `데이터 분석` 화면을 추가했다.

## Factory I/O 연결 전 체크리스트

- Coil / Discrete Input / Holding Register / Input Register의 시작 주소가 0-base인지 확인
- Start, Stop, Reset, E-Stop은 순간 신호인지 유지 신호인지 결정
- 명령(command)과 실제 상태(feedback)를 별도 태그로 구성
- 제품 통과 센서는 상승 에지 한 번만 집계
- Vision 결과 코드(정상/불량/색상/재질)의 숫자 규약 확정
- 통신 단절 시 출력 유지/초기화 정책과 재연결 후 동기화 정책 확정
