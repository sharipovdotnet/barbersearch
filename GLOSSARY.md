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

**Grade**:
An administrative rank of a Barber (Intern … Barbershop Owner). Not self-declared.
_Avoid_: Level, rank, tier

## Places

**Salon**:
A physical place that opens and closes in one time zone, with an address and coordinates.
_Avoid_: Shop, branch, location

**Barber Salon**:
The fact that one Barber works in one Salon, together with that Barber's weekly schedule at that
Salon and whether it is his primary Salon.
_Avoid_: Workplace, employment

**Primary Salon**:
The one Barber Salon that represents the Barber by default. A Barber has at most one.

**Working Schedule**:
A Barber's weekly availability at one Barber Salon: one weekday with opening hours, or a day off.
_Avoid_: Opening hours, timetable

**Time Off**:
A dated period during which a Barber does not work at all. Independent of the weekly schedule.
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
