using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ExpenseTracker.Validation;

namespace ExpenseTracker.Models.Domain
{
    /// <summary>Lifecycle of an <see cref="Event"/>. Completed/Cancelled are archived by default.</summary>
    public enum EventStatus
    {
        Planning = 1,
        Active = 2,
        Completed = 3,
        Cancelled = 4
    }

    /// <summary>
    /// Which starter template an event was created from. Purely descriptive after
    /// creation — the seeded sub-events are ordinary rows the user can edit or delete.
    /// </summary>
    public enum EventType
    {
        Custom = 0,
        Wedding = 1,
        BirthdayParty = 2,
        HouseWarming = 3,
        BabyShower = 4,
        Festival = 5,
        Trip = 6
    }

    /// <summary>
    /// A one-off project budget — a wedding, a birthday party, a house warming.
    /// Holds <see cref="SubEvent"/> line items, each with its own allocation and
    /// dated spend rows, plus optional money received (<see cref="EventContribution"/>).
    ///
    /// DELIBERATELY ISOLATED: no FK to Expense/Income/Account/Transfer, and
    /// <see cref="EventSpend"/> carries no AccountId. Nothing here participates in
    /// the monthly totals, the dashboard, account balances or net worth.
    /// </summary>
    public class Event
    {
        public int Id { get; set; }

        [Required, StringLength(100, MinimumLength = 1)]
        [Display(Name = "Event name")]
        public string Name { get; set; } = string.Empty;

        [Display(Name = "Event type")]
        public EventType EventType { get; set; } = EventType.Custom;

        [DataType(DataType.Date)]
        [Display(Name = "Event date")]
        public DateTime? EventDate { get; set; }

        public EventStatus Status { get; set; } = EventStatus.Planning;

        [StringLength(500)]
        public string? Note { get; set; }

        public string? UserId { get; set; }
        public ApplicationUser? User { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<SubEvent> SubEvents { get; set; } = new List<SubEvent>();
        public ICollection<EventContribution> Contributions { get; set; } = new List<EventContribution>();

        /// <summary>Completed and Cancelled events are hidden from the index unless "Show archived" is on.</summary>
        [NotMapped]
        public bool IsArchived => Status is EventStatus.Completed or EventStatus.Cancelled;
    }

    /// <summary>
    /// One budgeted line within an event — "Stage decoration", "Ornaments", "Wedding hall".
    /// Planned money is <see cref="Allocated"/>; actual money is the sum of <see cref="Spends"/>.
    /// </summary>
    public class SubEvent
    {
        public int Id { get; set; }

        public int EventId { get; set; }
        public Event? Event { get; set; }

        [Required, StringLength(100, MinimumLength = 1)]
        [Display(Name = "Sub-event")]
        public string Name { get; set; } = string.Empty;

        /// <summary>Planned budget. Zero is legal — it means "not budgeted yet".</summary>
        [Range(0, 999999999.99, ErrorMessage = "Allocated amount must be zero or more.")]
        [Display(Name = "Allocated")]
        public decimal Allocated { get; set; }

        /// <summary>Display order within the event. Assigned server-side on create.</summary>
        public int SortOrder { get; set; }

        [StringLength(200)]
        public string? Note { get; set; }

        public string? UserId { get; set; }
        public ApplicationUser? User { get; set; }

        public ICollection<EventSpend> Spends { get; set; } = new List<EventSpend>();
    }

    /// <summary>
    /// A dated payment against a sub-event — the advance, the balance, an extra.
    /// Never mirrored into the Expense ledger; see the isolation note on <see cref="Event"/>.
    /// </summary>
    public class EventSpend
    {
        public int Id { get; set; }

        public int SubEventId { get; set; }
        public SubEvent? SubEvent { get; set; }

        [Range(0.01, 999999999.99, ErrorMessage = "Amount must be greater than zero.")]
        public decimal Amount { get; set; }

        [Required, DataType(DataType.Date)]
        [NotInFuture]
        public DateTime Date { get; set; }

        [StringLength(120)]
        [Display(Name = "Paid to")]
        public string? PaidTo { get; set; }

        [StringLength(200)]
        public string? Note { get; set; }

        public string? UserId { get; set; }
        public ApplicationUser? User { get; set; }
    }

    /// <summary>
    /// Money received towards an event — wedding gifts, cash from relatives, a sponsor.
    /// Offsets the event's actual spend to give a net outlay. Event-level, not per sub-event.
    /// </summary>
    public class EventContribution
    {
        public int Id { get; set; }

        public int EventId { get; set; }
        public Event? Event { get; set; }

        [Range(0.01, 999999999.99, ErrorMessage = "Amount must be greater than zero.")]
        public decimal Amount { get; set; }

        [Required, DataType(DataType.Date)]
        [NotInFuture]
        public DateTime Date { get; set; }

        [StringLength(120)]
        [Display(Name = "From")]
        public string? FromWhom { get; set; }

        [StringLength(200)]
        public string? Note { get; set; }

        public string? UserId { get; set; }
        public ApplicationUser? User { get; set; }
    }
}
