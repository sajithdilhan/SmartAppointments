using Booking.Application.Models;
using Booking.Application.Queries;
using FluentValidation;

namespace Booking.Application.Validations;

public class ListMyAppointmentsQueryValidator : AbstractValidator<ListMyAppointmentsQuery>
{
    public ListMyAppointmentsQueryValidator()
    {
        RuleFor(x => x.Status)
            .Must(s => MyAppointmentsParameters.TryParseStatus(s, out _))
            .WithMessage("The status must be Booked or Cancelled.");
        RuleFor(x => x.When)
            .Must(w => MyAppointmentsParameters.TryParseWhen(w, out _))
            .WithMessage("The when must be upcoming or past.");
        RuleFor(x => x.Page)
            .Must(p => MyAppointmentsParameters.TryParsePage(p, out _))
            .WithMessage("The page must be a whole number of at least 1.");
        RuleFor(x => x.PageSize)
            .Must(p => MyAppointmentsParameters.TryParsePageSize(p, out _))
            .WithMessage($"The pageSize must be a whole number from 1 to {MyAppointmentsParameters.MaxPageSize}.");
    }
}
