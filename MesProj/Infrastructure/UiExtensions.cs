using System;
using System.Drawing;
using System.Windows.Forms;
using MesProj.Models;

namespace MesProj.Infrastructure
{
    public static class UiExtensions
    {
        public static void SafeInvoke(this Control control, Action action)
        {
            if (control == null || control.IsDisposed)
            {
                return;
            }

            if (control.InvokeRequired)
            {
                control.BeginInvoke(action);
                return;
            }

            action();
        }

        public static Color ToBackColor(this EquipmentState state)
        {
            switch (state)
            {
                case EquipmentState.Running:
                    return Color.FromArgb(220, 245, 225);
                case EquipmentState.Stopped:
                    return Color.FromArgb(238, 240, 243);
                case EquipmentState.Fault:
                    return Color.FromArgb(255, 224, 224);
                default:
                    return Color.FromArgb(232, 232, 232);
            }
        }

        public static Color ToForeColor(this EquipmentState state)
        {
            switch (state)
            {
                case EquipmentState.Running:
                    return Color.FromArgb(21, 107, 54);
                case EquipmentState.Fault:
                    return Color.FromArgb(166, 35, 35);
                case EquipmentState.Disconnected:
                    return Color.FromArgb(90, 90, 90);
                default:
                    return Color.FromArgb(45, 52, 60);
            }
        }

        public static string ToDisplayText(this EquipmentState state)
        {
            return state.ToString();
        }
    }
}
