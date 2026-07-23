using System;
using System.Threading;
using System.Threading.Tasks;
using MesProj.Infrastructure;

namespace MesProj.Services
{
    public sealed class BlueLidSorterController
    {
        private const int BlueLidIdentificationTimeoutMilliseconds = 3000;
        private const int SorterArrivalTimeoutMilliseconds = 10000;
        private const int SorterExitTimeoutMilliseconds = 5000;
        private const int SorterDischargeOverrunMilliseconds = 1500;
        private const int BlueLidBeltOverrunMilliseconds = 8500;

        private readonly object _syncRoot = new object();
        private BlueLidRouteState _routeState = BlueLidRouteState.Idle;
        private DateTime _routeDeadlineUtc = DateTime.MinValue;
        private bool _enabled;

        public void Reset()
        {
            lock (_syncRoot)
            {
                _enabled = false;
                ResetRouteState();
            }
        }

        public void ValidateOutputCommand(
            int address,
            bool value,
            Func<string, bool> getCommandState)
        {
            if (!value) return;

            if (address == FactoryIoMap.Sorter1BlueLid.OutputAddress &&
                getCommandState(FactoryIoMap.Sorter1GreenLid.Key))
            {
                throw new InvalidOperationException(
                    "Sorter 1 Blue Lid와 Green Lid는 동시에 ON할 수 없습니다.");
            }

            if (address == FactoryIoMap.Sorter1GreenLid.OutputAddress &&
                getCommandState(FactoryIoMap.Sorter1BlueLid.Key))
            {
                throw new InvalidOperationException(
                    "Sorter 1 Green Lid와 Blue Lid는 동시에 ON할 수 없습니다.");
            }
        }

        public async Task SetEnabledAsync(
            bool enabled,
            Func<EquipmentDefinition, bool, CancellationToken, Task> writeOutputAsync,
            CancellationToken cancellationToken)
        {
            if (enabled)
            {
                await writeOutputAsync(
                    FactoryIoMap.ExitBeltSorter1,
                    true,
                    cancellationToken).ConfigureAwait(false);

                lock (_syncRoot)
                {
                    _enabled = true;
                    ResetRouteState();
                }
            }
            else
            {
                lock (_syncRoot)
                {
                    _enabled = false;
                    ResetRouteState();
                }

                await StopOutputsAsync(writeOutputAsync, cancellationToken).ConfigureAwait(false);
            }

            AppLogger.Info("BLUE LID AUTO SORTING " + (enabled ? "ENABLED" : "DISABLED"));
        }

        public async Task HandleSensorEdgeAsync(
            EquipmentDefinition definition,
            bool risingEdge,
            bool fallingEdge,
            bool resetRecoveryPending,
            Func<EquipmentDefinition, bool, CancellationToken, Task> writeOutputAsync,
            CancellationToken cancellationToken)
        {
            if (definition.Key == FactoryIoMap.MachiningOutputSensor.Key && risingEdge)
            {
                var routeCanStart = false;
                lock (_syncRoot)
                {
                    routeCanStart = _enabled && !resetRecoveryPending &&
                        _routeState == BlueLidRouteState.Idle;
                    if (routeCanStart)
                    {
                        _routeState = BlueLidRouteState.AwaitingIdentification;
                        _routeDeadlineUtc = DateTime.UtcNow.AddMilliseconds(
                            BlueLidIdentificationTimeoutMilliseconds);
                    }
                }

                AppLogger.Info(routeCanStart
                    ? "BLUE LID ROUTE awaiting camera identification"
                    : "BLUE LID ROUTE ignored Write Sensor while route is active or reset recovery is pending");
                return;
            }

            if (definition.Key == FactoryIoMap.GreenLidCamera.Key && risingEdge)
            {
                var canceled = false;
                lock (_syncRoot)
                {
                    canceled = _routeState == BlueLidRouteState.AwaitingIdentification;
                    if (canceled) ResetRouteState();
                }

                if (canceled) AppLogger.Info("BLUE LID ROUTE canceled by Green Lid Camera");
                return;
            }

            if (definition.Key == FactoryIoMap.BlueLidCamera.Key && risingEdge)
            {
                var prepareRoute = false;
                lock (_syncRoot)
                {
                    prepareRoute = _routeState == BlueLidRouteState.AwaitingIdentification &&
                        DateTime.UtcNow <= _routeDeadlineUtc;
                }

                if (!prepareRoute)
                {
                    AppLogger.Info("BLUE LID ROUTE ignored camera signal without Write Sensor latch");
                    return;
                }

                await writeOutputAsync(
                    FactoryIoMap.Sorter1GreenLid,
                    false,
                    cancellationToken).ConfigureAwait(false);
                await writeOutputAsync(
                    FactoryIoMap.Sorter1BlueLid,
                    true,
                    cancellationToken).ConfigureAwait(false);
                await writeOutputAsync(
                    FactoryIoMap.BlueLidBelt1,
                    true,
                    cancellationToken).ConfigureAwait(false);

                lock (_syncRoot)
                {
                    _routeState = BlueLidRouteState.WaitingForSorter;
                    _routeDeadlineUtc = DateTime.UtcNow.AddMilliseconds(
                        SorterArrivalTimeoutMilliseconds);
                }

                AppLogger.Info("BLUE LID ROUTE prepared Coil 7 ON, Coil 8 OFF, Coil 9 ON");
                return;
            }

            if (definition.Key != FactoryIoMap.ReadSensorSorter1.Key) return;

            if (risingEdge)
            {
                var startSorter = false;
                lock (_syncRoot)
                {
                    startSorter = _routeState == BlueLidRouteState.WaitingForSorter;
                }

                if (!startSorter) return;

                await writeOutputAsync(
                    FactoryIoMap.Sorter1ForwardAndPower,
                    true,
                    cancellationToken).ConfigureAwait(false);

                lock (_syncRoot)
                {
                    _routeState = BlueLidRouteState.Routing;
                    _routeDeadlineUtc = DateTime.UtcNow.AddMilliseconds(
                        SorterExitTimeoutMilliseconds);
                }

                AppLogger.Info("BLUE LID ROUTE started Coil 6 ON");
                return;
            }

            if (!fallingEdge) return;

            var finishSorter = false;
            lock (_syncRoot)
            {
                finishSorter = _routeState == BlueLidRouteState.Routing;
            }

            if (!finishSorter) return;

            lock (_syncRoot)
            {
                _routeState = BlueLidRouteState.ClearingSorter;
                _routeDeadlineUtc = DateTime.UtcNow.AddMilliseconds(
                    SorterDischargeOverrunMilliseconds);
            }

            AppLogger.Info(
                "BLUE LID ROUTE entry sensor cleared, Coil 6, 7 and 9 discharge overrun started");
        }

        public async Task CheckTimersAsync(
            Func<EquipmentDefinition, bool, CancellationToken, Task> writeOutputAsync,
            Action<string, string> raiseAlarm,
            CancellationToken cancellationToken)
        {
            BlueLidRouteState expiredState;
            lock (_syncRoot)
            {
                if (_routeState == BlueLidRouteState.Idle ||
                    DateTime.UtcNow < _routeDeadlineUtc)
                {
                    return;
                }

                expiredState = _routeState;
                ResetRouteState();
            }

            if (expiredState == BlueLidRouteState.AwaitingIdentification)
            {
                AppLogger.Info("BLUE LID ROUTE identification window expired");
                return;
            }

            if (expiredState == BlueLidRouteState.ClearingBlueLidBelt)
            {
                await writeOutputAsync(
                    FactoryIoMap.BlueLidBelt1,
                    false,
                    cancellationToken).ConfigureAwait(false);
                AppLogger.Info("BLUE LID ROUTE completed Coil 9 OFF");
                return;
            }

            if (expiredState == BlueLidRouteState.ClearingSorter)
            {
                await writeOutputAsync(
                    FactoryIoMap.Sorter1ForwardAndPower,
                    false,
                    cancellationToken).ConfigureAwait(false);
                await writeOutputAsync(
                    FactoryIoMap.Sorter1BlueLid,
                    false,
                    cancellationToken).ConfigureAwait(false);

                lock (_syncRoot)
                {
                    _routeState = BlueLidRouteState.ClearingBlueLidBelt;
                    _routeDeadlineUtc = DateTime.UtcNow.AddMilliseconds(
                        BlueLidBeltOverrunMilliseconds);
                }

                AppLogger.Info(
                    "BLUE LID ROUTE sorter discharge completed, Coil 9 overrun started");
                return;
            }

            await StopOutputsAsync(writeOutputAsync, cancellationToken).ConfigureAwait(false);
            raiseAlarm(
                expiredState == BlueLidRouteState.WaitingForSorter
                    ? "SORTER_1_ARRIVAL_TIMEOUT"
                    : "SORTER_1_EXIT_TIMEOUT",
                expiredState == BlueLidRouteState.WaitingForSorter
                    ? "Blue Lid가 제한시간 안에 Sorter 1에 도착하지 않았습니다."
                    : "Blue Lid가 제한시간 안에 Sorter 1을 통과하지 못했습니다.");
        }

        public async Task StopOutputsAsync(
            Func<EquipmentDefinition, bool, CancellationToken, Task> writeOutputAsync,
            CancellationToken cancellationToken)
        {
            await writeOutputAsync(
                FactoryIoMap.ExitBeltSorter1,
                false,
                cancellationToken).ConfigureAwait(false);
            await writeOutputAsync(
                FactoryIoMap.Sorter1ForwardAndPower,
                false,
                cancellationToken).ConfigureAwait(false);
            await writeOutputAsync(
                FactoryIoMap.Sorter1BlueLid,
                false,
                cancellationToken).ConfigureAwait(false);
            await writeOutputAsync(
                FactoryIoMap.Sorter1GreenLid,
                false,
                cancellationToken).ConfigureAwait(false);
            await writeOutputAsync(
                FactoryIoMap.BlueLidBelt1,
                false,
                cancellationToken).ConfigureAwait(false);

            lock (_syncRoot)
            {
                ResetRouteState();
            }
        }

        private void ResetRouteState()
        {
            _routeState = BlueLidRouteState.Idle;
            _routeDeadlineUtc = DateTime.MinValue;
        }

        private enum BlueLidRouteState
        {
            Idle,
            AwaitingIdentification,
            WaitingForSorter,
            Routing,
            ClearingSorter,
            ClearingBlueLidBelt
        }
    }
}
