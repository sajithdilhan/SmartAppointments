using Auth.Application.Commands;
using Auth.Application.Validations;

namespace Auth.Tests;

public class AssignStaffBranchCommandValidatorTests
{
    private readonly AssignStaffBranchCommandValidator _subject = new();

    [Fact]
    public void A_Branch_Passes()
    {
        Assert.True(_subject.Validate(new AssignStaffBranchCommand(Guid.CreateVersion7(), Guid.CreateVersion7())).IsValid);
    }

    [Fact]
    public void An_Empty_Branch_Fails()
    {
        var result = _subject.Validate(new AssignStaffBranchCommand(Guid.CreateVersion7(), Guid.Empty));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AssignStaffBranchCommand.BranchId));
    }
}
