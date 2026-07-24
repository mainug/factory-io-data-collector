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
        public static readonly EquipmentDefinition BlueBaseBelt1 = new EquipmentDefinition("BlueBaseBelt1", "Blue Base Belt 1", 0);
        public static readonly EquipmentDefinition BlueBaseBelt2 = new EquipmentDefinition("BlueBaseBelt2", "Blue Base Belt 2", 1);
        public static readonly EquipmentDefinition BlueBaseBelt3 = new EquipmentDefinition("BlueBaseBelt3", "Blue Base Belt 3", 2);
        public static readonly EquipmentDefinition BlueBaseRoller1 = new EquipmentDefinition("BlueBaseRoller1", "Blue Base Roller 1", 3);
        public static readonly EquipmentDefinition BlueBaseRoller2 = new EquipmentDefinition("BlueBaseRoller2", "Blue Base Roller 2", 4);
        public static readonly EquipmentDefinition BlueBaseRoller3 = new EquipmentDefinition("BlueBaseRoller3", "Blue Base Roller 3", 5);
        public static readonly EquipmentDefinition BlueBaseRoller4 = new EquipmentDefinition("BlueBaseRoller4", "Blue Base Roller 4", 6);
        public static readonly EquipmentDefinition BlueBaseConvRollerSensor = new EquipmentDefinition("BlueBaseConvRollerSensor", "Blue Base Conv Roller Sensor", -1, 0);
        public static readonly EquipmentDefinition BlueBaseStopRollerSensor = new EquipmentDefinition("BlueBaseStopRollerSensor", "Blue Base Stop Roller Sensor", -1, 1);
        public static readonly EquipmentDefinition BlueBasePositionerSensor = new EquipmentDefinition("BlueBasePositionerSensor", "Blue Base Positioner Sensor", -1, 2);
        public static readonly EquipmentDefinition BlueBaseCamera = new EquipmentDefinition("BlueBaseCamera", "Blue Base Camera", -1, 3);
        public static readonly EquipmentDefinition ReadSensorSorter2 = new EquipmentDefinition("ReadSensorSorter2", "Read Sensor Sorter 2", -1, 4);
        public static readonly EquipmentDefinition BlueBaseGrabSensor = new EquipmentDefinition("BlueBaseGrabSensor", "Blue Base Grab Sensor", -1, 5);
        public static readonly EquipmentDefinition StackerCraneBlueBaseLoaderSensor = new EquipmentDefinition("StackerCraneBlueBaseLoaderSensor", "Stacker Crane Blue Base Loader Sensor", -1, 6);
        public static readonly EquipmentDefinition StopEntranceBeltSensor = new EquipmentDefinition("StopEntranceBeltSensor", "Stop Entrance Belt Sensor", -1, 7);
        public static readonly EquipmentDefinition MachiningEntranceBelt = new EquipmentDefinition("MachiningEntranceBelt", "Entrance belt", 0);
        public static readonly EquipmentDefinition MachiningProductType = new EquipmentDefinition("MachiningProductType", "Machining Type (ON=Lid, OFF=Base)", 1);
        public static readonly EquipmentDefinition MachiningStart = new EquipmentDefinition("MachiningStart", "Machining Center Start", 2, -1, true);
        public static readonly EquipmentDefinition MachiningStop = new EquipmentDefinition("MachiningStop", "Machining Center Stop", 3, -1, true);
        public static readonly EquipmentDefinition MachiningReset = new EquipmentDefinition("MachiningReset", "Machining Center Reset", 4, -1, true);
        public static readonly EquipmentDefinition ExitBeltSorter1 = new EquipmentDefinition("ExitBeltSorter1", "Exit Belt Sorter 1", 5);
        public static readonly EquipmentDefinition Sorter1ForwardAndPower = new EquipmentDefinition("Sorter1ForwardAndPower", "Sorter 1 Forward and Power", 6);
        public static readonly EquipmentDefinition Sorter1BlueLid = new EquipmentDefinition("Sorter1BlueLid", "Sorter 1 Blue Lid", 7);
        public static readonly EquipmentDefinition Sorter1GreenLid = new EquipmentDefinition("Sorter1GreenLid", "Sorter 1 Green Lid", 8);
        public static readonly EquipmentDefinition BlueLidBelt1 = new EquipmentDefinition("BlueLidBelt1", "Blue Lid Belt 1", 9);
        public static readonly EquipmentDefinition GreenLidBelt1 = new EquipmentDefinition("GreenLidBelt1", "Green Lid Belt 1", 10);
        public static readonly EquipmentDefinition GreenLidBelt2 = new EquipmentDefinition("GreenLidBelt2", "Green Lid Belt 2", 11);
        public static readonly EquipmentDefinition RightPositioner4Raise = new EquipmentDefinition("RightPositioner4Raise", "Right Positioner 4 Raise", 12);
        public static readonly EquipmentDefinition GreenLidGrab = new EquipmentDefinition("GreenLidGrab", "Green Lid Grab", 13);
        public static readonly EquipmentDefinition GreenLidPositionerClamp = new EquipmentDefinition("GreenLidPositionerClamp", "Green Lid Positioner Clamp", 14);
        public static readonly EquipmentDefinition GreenLidRoller1 = new EquipmentDefinition("GreenLidRoller1", "Green Lid Roller 1", 15);
        public static readonly EquipmentDefinition GreenLidRoller2 = new EquipmentDefinition("GreenLidRoller2", "Green Lid Roller 2", 16);
        public static readonly EquipmentDefinition GreenLidRoller3 = new EquipmentDefinition("GreenLidRoller3", "Green Lid Roller 3", 17);
        public static readonly EquipmentDefinition GreenLidRoller4 = new EquipmentDefinition("GreenLidRoller4", "Green Lid Roller 4", 18);
        public static readonly EquipmentDefinition GreenLidRoller5 = new EquipmentDefinition("GreenLidRoller5", "Green Lid Roller 5", 19);
        public static readonly EquipmentDefinition StackerCraneGreenLidLeft = new EquipmentDefinition("StackerCraneGreenLidLeft", "Stacker Crane Green Lid Left", 20);
        public static readonly EquipmentDefinition MachiningEntranceSensor = new EquipmentDefinition("MachiningEntranceSensor", "Stop Entrance Belt Sensor", -1, 0);
        public static readonly EquipmentDefinition MachiningBusy = new EquipmentDefinition("MachiningBusy", "Machining Center Is Busy", -1, 1);
        public static readonly EquipmentDefinition MachiningError = new EquipmentDefinition("MachiningError", "Machining Center Has Error", -1, 2);
        public static readonly EquipmentDefinition MachiningOpened = new EquipmentDefinition("MachiningOpened", "Machining Center Opened", -1, 3);
        public static readonly EquipmentDefinition MachiningOutputSensor = new EquipmentDefinition("MachiningOutputSensor", "Write Sensor", -1, 4);
        public static readonly EquipmentDefinition ReadSensorSorter1 = new EquipmentDefinition("ReadSensorSorter1", "Read Sensor Sorter 1", -1, 5);
        public static readonly EquipmentDefinition BlueLidCamera = new EquipmentDefinition("BlueLidCamera", "Blue Lid Camera", -1, 6);
        public static readonly EquipmentDefinition GreenLidCamera = new EquipmentDefinition("GreenLidCamera", "Green Lid Camera", -1, 7);
        public static readonly EquipmentDefinition RightPositioner4Limit = new EquipmentDefinition("RightPositioner4Limit", "Right Positioner 4 Limit", -1, 8);
        public static readonly EquipmentDefinition GreenLidStopRollerSensor = new EquipmentDefinition("GreenLidStopRollerSensor", "Green Lid Stop Roller Sensor", -1, 9);
        public static readonly EquipmentDefinition GreenLidGrabSensor = new EquipmentDefinition("GreenLidGrabSensor", "Green Lid Grab Sensor", -1, 10);
        public static readonly EquipmentDefinition GreenLidPositionerSensor = new EquipmentDefinition("GreenLidPositionerSensor", "Green Lid Positioner Sensor", -1, 11);
        public static readonly EquipmentDefinition GreenLidPositionerClampSensor = new EquipmentDefinition("GreenLidPositionerClampSensor", "Green Lid Positioner Clamp Sensor", -1, 12);
        public static readonly EquipmentDefinition GreenLidXMovingSensor = new EquipmentDefinition("GreenLidXMovingSensor", "Green Lid X Moving Sensor", -1, 13);
        public static readonly EquipmentDefinition GreenLidZMovingSensor = new EquipmentDefinition("GreenLidZMovingSensor", "Green Lid Z Moving Sensor", -1, 14);

        public const int AtExitSensorInput = 0;
        public const int MachiningProgressRegister = 0;
        public const int GreenLidXSetPointRegister = 0;
        public const int GreenLidZSetPointRegister = 1;
        public const int GreenLidXPositionRegister = 1;
        public const int GreenLidZPositionRegister = 2;

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
                InputAddress = definition.FeedbackInputAddress,
                IsPulseOutput = definition.IsPulseOutput,
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
        public bool IsPulseOutput { get; private set; }

        public EquipmentDefinition(string key, string name, int outputAddress, int feedbackInputAddress = -1, bool isPulseOutput = false)
        {
            Key = key;
            Name = name;
            OutputAddress = outputAddress;
            FeedbackInputAddress = feedbackInputAddress;
            IsPulseOutput = isPulseOutput;
        }
    }
}
