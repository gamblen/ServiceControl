﻿namespace ServiceControl.Config.UI.MessageBox
{
    using System;
    using System.Windows.Input;
    using Caliburn.Micro;
    using Framework;
    using Framework.Rx;
    using Xaml.Controls;

    public class SliderDialogViewModel : RxScreen
    {
        public SliderDialogViewModel(string title,
            string message,
            string periodHeader,
            string periodExplanation,
            TimeSpanUnits periodUnits,
            int periodMinimumUnits,
            int periodMaximumUnits,
            int periodSmallStep,
            int periodLargeStep,
            int currentValue)
        {
            Title = title;
            Message = message;
            Value = currentValue;
            PeriodHeader = periodHeader;
            PeriodUnits = periodUnits;
            PeriodMinimum = periodMinimumUnits;
            PeriodMaximum = periodMaximumUnits;
            PeriodExplanation = periodExplanation;
            PeriodSmallStep = periodSmallStep;
            PeriodLargeStep = periodLargeStep;
            Cancel = Command.Create(async () =>
            {
                Result = null;
                await ((IDeactivate)this).DeactivateAsync(true);
            });
            Save = Command.Create(async () =>
            {
                Result = true;
                await ((IDeactivate)this).DeactivateAsync(true);
            });
        }

        public string PeriodHeader { get; set; }
        public string PeriodExplanation { get; set; }
        public int PeriodMinimum { get; set; }
        public int PeriodMaximum { get; set; }
        public int PeriodSmallStep { get; set; }
        public int PeriodLargeStep { get; set; }

        public TimeSpanUnits PeriodUnits { get; set; }

        public double Value { get; set; }

        public TimeSpan Period => PeriodUnits == TimeSpanUnits.Days ? TimeSpan.FromDays(Value) : TimeSpan.FromHours(Value);

        public string Title { get; set; }

        public string Message { get; set; }

        public ICommand Cancel { get; }
        public ICommand Save { get; }
    }
}