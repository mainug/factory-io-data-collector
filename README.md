# 🏭 Factory I/O Data Collector

`factory-io-data-collector`는 **Factory I/O** 3D 산업 시뮬레이션 환경에서 센서, 액추에이터, 프로세스 데이터를 실시간으로 수집하고 저장/전송하기 위한 C#/.NET 기반 데이터 수집기(Data Collector) 프로젝트입니다.

---

## 📌 Key Features (주요 기능)

- **Real-time Data Acquisition**: Factory I/O 태그(Sensors/Actuators) 데이터 실시간 수집
- **Protocol Support**: Modbus TCP, OPC UA 또는 Factory I/O SDK 연동 지원
- **Data Export & Logging**: 수집된 데이터를 CSV, Database(MSSQL / PostgreSQL / InfluxDB) 또는 MQTT로 전송
- **High Performance**: C# 비동기(Async/Await) 처리를 통한 안정적이고 빠른 데이터 수집

---

## 🛠 Tech Stack (기술 스택)

- **Simulation**: Factory I/O
- **Framework**: .NET 8.0 (or .NET Framework / .NET Core)
- **Language**: C# 12
- **Libraries**:
  - `NModbus` / `FluentFTP` / `System.Net.Sockets` (통신 라이브러리)
  - `Serilog` (로그 기록)
  - `Dapper` / `Entity Framework Core` (데이터베이스 연동)

---

## 🏗 Architecture (시스템 구조)

```text
[ Factory I/O ] ──(Modbus TCP / SDK)──> [ C# Data Collector ] ──> [ DB / Dashboard ]
```
