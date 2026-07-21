using System.Collections.Generic;
using MesProj.Models;

namespace MesProj.Infrastructure
{
    public static class FactoryIoMap
    {
        // TODO: Factory I/O 씬 확정 후 실제 Modbus Coil/Input/Register 주소로 교체한다.
        public static readonly EquipmentDefinition EntryConveyor = new EquipmentDefinition("EntryConveyor", "투입 컨베이어", 0);
        public static readonly EquipmentDefinition StopBlade = new EquipmentDefinition("StopBlade", "스토퍼", 1);
        public static readonly EquipmentDefinition ExitConveyor = new EquipmentDefinition("ExitConveyor", "배출 컨베이어", 2);
        public static readonly EquipmentDefinition Sorter1Turn = new EquipmentDefinition("Sorter1Turn", "분류기 1 회전", 3);
        public static readonly EquipmentDefinition Sorter1Belt = new EquipmentDefinition("Sorter1Belt", "분류기 1 벨트", 4);
        public static readonly EquipmentDefinition Sorter2Turn = new EquipmentDefinition("Sorter2Turn", "분류기 2 회전", 5);
        public static readonly EquipmentDefinition Sorter2Belt = new EquipmentDefinition("Sorter2Belt", "분류기 2 벨트", 6);
        public static readonly EquipmentDefinition Sorter3Turn = new EquipmentDefinition("Sorter3Turn", "분류기 3 회전", 7);
        public static readonly EquipmentDefinition Sorter3Belt = new EquipmentDefinition("Sorter3Belt", "분류기 3 벨트", 8);
        public static readonly EquipmentDefinition VisionSensor = new EquipmentDefinition("VisionSensor", "비전 센서", -1);
        public static readonly EquipmentDefinition Emitter = new EquipmentDefinition("Emitter", "제품 생성기", 13);
        public static readonly EquipmentDefinition BlueBaseBelt1Pilot = new EquipmentDefinition("BlueBaseBelt1", "Blue Base Belt 1", 0, 0);

        public const int AtExitSensorInput = 0;
        public const int VisionRegister = 0;

        public static IReadOnlyList<EquipmentDefinition> AllEquipment
        {
            get
            {
                return new[]
                {
                    EntryConveyor, ExitConveyor, StopBlade, Sorter1Turn, Sorter1Belt,
                    Sorter2Turn, Sorter2Belt, Sorter3Turn, Sorter3Belt, VisionSensor, Emitter
                };
            }
        }

        public static EquipmentStatus CreateStatus(EquipmentDefinition definition, EquipmentState state)
        {
            return new EquipmentStatus
            {
                Key = definition.Key,
                Name = definition.Name,
                OutputAddress = definition.OutputAddress,
                State = state,
                CommandState = false,
                FeedbackState = false,
                LastChangedAt = System.DateTime.Now
            };
        }
    }

    public sealed class EquipmentDefinition
    {
        public string Key { get; private set; }
        public string Name { get; private set; }
        public int OutputAddress { get; private set; }
        public int FeedbackInputAddress { get; private set; }

        public EquipmentDefinition(string key, string name, int outputAddress, int feedbackInputAddress = -1)
        {
            Key = key;
            Name = name;
            OutputAddress = outputAddress;
            FeedbackInputAddress = feedbackInputAddress;
        }
    }
}
