# BarberSearch

Backend context for a barber discovery and online-booking service. A person searches for a
barber, picks a salon and a service, and books a time slot.

## People and accounts

**User**:
An account that can sign in. Every User has exactly one role.
_Avoid_: Account, member, person

**Customer**:
A User who books services. The default role.
_Avoid_: Client, buyer

**Barber**:
A User who provides services in one or more salons.
_Avoid_: Master, stylist, hairdresser

**Admin**:
A User who manages the catalogue and grants sensitive attributes.

**Barber Profile**:
The public identity of a Barber: display name, experience, bio, ratings, portfolio, prices and
weekly availability. A User without a Barber Profile is not a Barber.
_Avoid_: Master card, barber card

**Barber Request**:
A Customer's application for the Barber role. A Customer has at most one active request; an
administrator approves it or rejects it with a reason. It grants no rights by itself: the role
changes only on approval, and the Barber Profile is created afterwards. Not the same as a Barber
Profile.
_Avoid_: Barber application, role request

**Grade**:
An administrative rank of a Barber (Intern … Barbershop Owner). Not self-declared.
_Avoid_: Level, rank, tier

## Places

**Salon**:
A physical place that opens and closes in one time zone, with an address and coordinates.
_Avoid_: Shop, branch, location

**Salon Owner**:
The User who owns a Salon. It is a link (`Salon.OwnerUserId`), not a role: a Salon has at most
one owner, the owner is always a Barber with a Barber Profile, and there is no fourth role.
A Salon with no owner was created by an administrator. The owner can edit the address and
contacts, manage the Salon's barbers (attach and detach other Barbers), but cannot activate,
hide or rename the Salon.
_Avoid_: Salon admin, shop owner role

**Salon Barber Composition**:
The set of Barber Salon links that belong to one Salon. The Salon Owner reads it, searches
candidates by name or email, attaches another Barber Profile (the first Salon of that profile
becomes primary) and detaches one. The owner's own link cannot be removed: it is created with
the Salon and keeps ownership working. The list includes unverified and disabled profiles so
the owner can remove them; public visibility still requires an active, verified profile.
_Avoid_: Salon staff, team, employees

**Salon Moderation Status**:
The state of a Salon before and after an administrator's review: Pending → Active | Rejected.
It is not IsActive (visibility): the Salon is publicly visible only when it is both active and
IsActive. Salons created by an administrator start Active; a Salon created by its Barber starts
Pending. Rejected means the administrator declined it with a reason; the owner cannot resubmit
in this slice.
_Avoid_: Approval flag, publication status

**Barber Salon**:
The fact that one Barber works in one Salon, together with that Barber's weekly schedule at that
Salon and whether it is his primary Salon.
_Avoid_: Workplace, employment

**Primary Salon**:
The one Barber Salon that represents the Barber by default. A Barber has at most one.

**Working Schedule**:
A Barber's weekly availability at one Barber Salon: one weekday with opening hours, or a day off.
_Avoid_: Opening hours, timetable

**Schedule Exception**:
One date on which a Barber Salon's weekly schedule does not apply: a day off or custom hours.
It belongs to one Barber Salon, unlike a Time Off. The code calls it `ScheduleOverride` because
CA1711 reserves the `Exception` suffix for error types.
_Avoid_: Override, special day, date override

**Time Off**:
A dated period during which a Barber does not work at all. It applies to the whole Barber Profile,
independent of the weekly schedule and of any Schedule Exception.
_Avoid_: Vacation, holiday, day off

## Catalogue

**Hair Service**:
A service offered by the platform (haircut, beard trim, colouring …), with a default duration.
_Avoid_: Service, procedure

**Barber Service**:
The price and duration at which one Barber offers one Hair Service.
_Avoid_: Price list entry, offering

**Specialization**:
A skill label from the platform's directory (fade master, beard master …).
_Avoid_: Skill, tag, category

**Barber Specialization**:
The fact that one Barber has one Specialization.
_Avoid_: Skill assignment

**Portfolio Photo**:
A published photograph of a Barber's work, with a sort order.
_Avoid_: Work sample, gallery item

## Booking and reputation

**Booking**:
One Customer's reservation of one Barber Service at one Salon for one time slot. Its life cycle
is Pending → Confirmed → Completed | Cancelled | No-Show.
_Avoid_: Appointment, order, reservation

**Cancellation Reason**:
The short explanation a Barber must give when cancelling a Booking. It is stored on the Booking,
shown to the Customer in the visit history, and is not the Barber's private note.
_Avoid_: Comment, excuse, barber note

**No-Show**:
A confirmed Booking where the Customer never arrived.
_Avoid_: Missed appointment

**Idempotency Key**:
A client-supplied string that makes a repeated Booking request return the original Booking instead
of creating a second one.
_Avoid_: Request id, dedupe key

**Review**:
A Customer's rating and comment about one Booking. It always belongs to a Booking, and its Barber
is derived from that Booking.
_Avoid_: Feedback, comment

**Rating**:
A number on the 1.00–5.00 scale, for both a Review (`1..5`) and a Barber Profile's average.
A Barber Profile with no reviews has no Rating at all (not zero).
_Avoid_: Score, stars, points

**Reviews Count**:
How many published Reviews the average Rating was computed from. Zero means there is no Rating.

**Favorite Barber**:
The fact that one User marked one Barber Profile as a favourite.
_Avoid_: Bookmark, saved barber
