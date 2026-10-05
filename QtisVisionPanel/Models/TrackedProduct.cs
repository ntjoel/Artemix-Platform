using System;
using System.Collections.Generic;

namespace QtisVisionPanel.Models
{
    public enum TrackedProductState
    {
        Detected,
        AwaitingInspection,
        Accepted,
        RejectedPending,
        RejectedExecuted,
        Completed
    }

    public class TrackedProduct
    {
        public long ProductId { get; set; }

        public DateTime DetectionTimeUtc { get; set; }

        public long DetectionEncoderCount { get; set; }

        public double DetectionMachinePositionMm { get; set; }

        public long TriggerEncoderTarget { get; set; }

        public long RejectEncoderTarget { get; set; }

        public long CurrentEncoderCount { get; set; }

        public double CurrentMachinePositionMm { get; set; }

        public bool? IsAccepted { get; set; }

        public string InspectionSource { get; set; }

        public string Notes { get; set; }

        public TrackedProductState State { get; set; } = TrackedProductState.Detected;

        public bool TriggerReached { get; set; }

        public bool RejectExecuted { get; set; }

        /// <summary>Prevents a second automatic actuation after a partial hardware failure.</summary>
        public bool RejectAttempted { get; set; }

        public List<ProductInterventionState> InterventionStates { get; set; } = new List<ProductInterventionState>();
    }

    public class ProductInterventionState
    {
        public string PointCode { get; set; }

        public string Description { get; set; }

        public string ActionType { get; set; }

        public string SignalCode { get; set; }

        public long TargetEncoderCount { get; set; }

        public double TargetOffsetMm { get; set; }

        public double AbsoluteMachineQuotaMm { get; set; }

        public int PulseMs { get; set; }

        public bool Executed { get; set; }

        public long ReachedEncoderCount { get; set; }

        public DateTime? ReachedTimeUtc { get; set; }

        public long ReachedLateCounts { get; set; }

        public double ReachedLateMm { get; set; }
    }

    public class TrackedProductEventArgs : EventArgs
    {
        public TrackedProductEventArgs(TrackedProduct product, string message = null)
        {
            Product = product;
            Message = message;
        }

        public TrackedProduct Product { get; }

        public string Message { get; }
    }

    public class ProductInterventionEventArgs : EventArgs
    {
        public ProductInterventionEventArgs(TrackedProduct product, ProductInterventionState interventionState, string message = null)
        {
            Product = product;
            InterventionState = interventionState;
            Message = message;
        }

        public TrackedProduct Product { get; }

        public ProductInterventionState InterventionState { get; }

        public string Message { get; }
    }
}
