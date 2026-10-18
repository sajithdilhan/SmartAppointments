using Auth.Application.Queries;
using Auth.Application.Validations;

namespace Auth.Tests;

public class LookupUsersQueryValidatorTests
{
    private readonly LookupUsersQueryValidator _subject = new();

    private static List<Guid> Ids(int count) => Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToList();

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public void One_To_One_Hundred_Ids_Pass(int count)
    {
        Assert.True(_subject.Validate(new LookupUsersQuery(Ids(count))).IsValid);
    }

    [Fact]
    public void Duplicates_Are_Allowed()
    {
        var id = Guid.NewGuid();

        Assert.True(_subject.Validate(new LookupUsersQuery([id, id])).IsValid);
    }

    [Fact]
    public void Missing_Ids_Fail()
    {
        Assert.False(_subject.Validate(new LookupUsersQuery(null)).IsValid);
    }

    [Fact]
    public void An_Empty_List_Fails()
    {
        Assert.False(_subject.Validate(new LookupUsersQuery([])).IsValid);
    }

    [Fact]
    public void More_Than_One_Hundred_Ids_Fail()
    {
        Assert.False(_subject.Validate(new LookupUsersQuery(Ids(101))).IsValid);
    }

    [Fact]
    public void An_Empty_Guid_Fails()
    {
        var result = _subject.Validate(new LookupUsersQuery([Guid.NewGuid(), Guid.Empty]));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "Ids cannot be empty GUIDs.");
    }
}
