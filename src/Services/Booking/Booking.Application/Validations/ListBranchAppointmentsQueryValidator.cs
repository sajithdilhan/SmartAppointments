using Booking.Application.Models;
using Booking.Application.Queries;
using FluentValidation;

namespace Booking.Application.Validations;

public class ListBranchAppointmentsQueryValidator : AbstractValidator<ListBranchAppointmentsQuery>
{
    public ListBranchAppointmentsQueryValidator()
    {
        RuleFor(x => x.Date)
            .Must(d => BranchAppointmentsParameters.TryParseDate(d, out _))
            .WithMessage("The date must be a valid date in the format yyyy-MM-dd.");
        RuleFor(x => x.Status)
            .Must(s => BranchAppointmentsParameters.TryParseStatus(s, out _))
            .WithMessage("The status must be Booked, Cancelled, Completed or NoShow.");
        RuleFor(x => x.Page)
            .Must(p => BranchAppointmentsParameters.TryParsePage(p, out _))
            .WithMessage("The page must be a whole number of at least 1.");
        RuleFor(x => x.PageSize)
            .Must(p => BranchAppointmentsParameters.TryParsePageSize(p, out _))
            .WithMessage($"The pageSize must be a whole number from 1 to {MyAppointmentsParameters.MaxPageSize}.");
    }
}
