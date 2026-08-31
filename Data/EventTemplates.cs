using ExpenseTracker.Models.Domain;

namespace ExpenseTracker.Data
{
    /// <summary>
    /// Starter sub-event lists for a new <see cref="Event"/>. Same static shape as
    /// <see cref="CategoryDefaults"/>. Seeded rows land with a zero allocation — the
    /// user fills in the amounts, and can rename or delete any of them afterwards.
    /// </summary>
    public static class EventTemplates
    {
        public static readonly IReadOnlyDictionary<EventType, string[]> SubEvents =
            new Dictionary<EventType, string[]>
            {
                [EventType.Wedding] = new[]
                {
                    "Wedding hall", "Stage decoration", "Catering", "Ornaments", "Clothing",
                    "Makeup & grooming", "Photography & video", "Invitations", "Music / DJ",
                    "Priest & rituals", "Transport", "Return gifts", "Miscellaneous"
                },
                [EventType.BirthdayParty] = new[]
                {
                    "Venue", "Cake", "Catering", "Decoration", "Photography",
                    "Return gifts", "Entertainment", "Invitations"
                },
                [EventType.HouseWarming] = new[]
                {
                    "Pooja & priest", "Catering", "Decoration", "Groceries",
                    "Gifts", "Photography", "Transport"
                },
                [EventType.BabyShower] = new[]
                {
                    "Venue", "Catering", "Decoration", "Clothing & gifts",
                    "Photography", "Invitations", "Return gifts"
                },
                [EventType.Festival] = new[]
                {
                    "Pooja & rituals", "Sweets & snacks", "New clothes", "Decoration",
                    "Gifts", "Crackers", "Guests & hosting"
                },
                [EventType.Trip] = new[]
                {
                    "Travel & tickets", "Stay", "Food", "Local transport",
                    "Sightseeing & tickets", "Shopping", "Buffer"
                },
                [EventType.Custom] = Array.Empty<string>()
            };

        /// <summary>Human label for an event type — used in the create form and exports.</summary>
        public static string Label(EventType type) => type switch
        {
            EventType.Wedding => "Wedding",
            EventType.BirthdayParty => "Birthday party",
            EventType.HouseWarming => "House warming",
            EventType.BabyShower => "Baby shower",
            EventType.Festival => "Festival",
            EventType.Trip => "Trip / vacation",
            _ => "Custom"
        };

        /// <summary>Bootstrap icon name for an event type.</summary>
        public static string Icon(EventType type) => type switch
        {
            EventType.Wedding => "gem",
            EventType.BirthdayParty => "balloon",
            EventType.HouseWarming => "house-heart",
            EventType.BabyShower => "emoji-smile",
            EventType.Festival => "stars",
            EventType.Trip => "airplane",
            _ => "calendar-event"
        };

        /// <summary>
        /// Build (but do not save) the template sub-events for a freshly created event.
        /// The caller owns SaveChanges.
        /// </summary>
        public static List<SubEvent> BuildFor(EventType type, int eventId, string userId)
        {
            if (!SubEvents.TryGetValue(type, out var names))
                return new List<SubEvent>();

            return names.Select((name, i) => new SubEvent
            {
                EventId = eventId,
                Name = name,
                Allocated = 0m,
                SortOrder = i,
                UserId = userId
            }).ToList();
        }
    }
}
