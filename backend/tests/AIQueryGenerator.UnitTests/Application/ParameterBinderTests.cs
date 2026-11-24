using AIQueryGenerator.Application.Sql;
using AIQueryGenerator.Shared.Exceptions;

namespace AIQueryGenerator.UnitTests.Application;

public class ParameterBinderTests
{
    [Theory]
    [InlineData(SqlParameterType.Int, "42", 42)]
    [InlineData(SqlParameterType.Bool, "true", true)]
    [InlineData(SqlParameterType.Bool, "0", false)]
    public void Converts_values(SqlParameterType type, string raw, object expected)
    {
        Assert.True(ParameterBinder.TryConvert(type, raw, out var value));
        Assert.Equal(expected, value);
    }

    [Fact]
    public void Converts_decimal_and_dates_with_invariant_culture()
    {
        Assert.True(ParameterBinder.TryConvert(SqlParameterType.Decimal, "1234.56", out var d));
        Assert.Equal(1234.56m, d);
        Assert.True(ParameterBinder.TryConvert(SqlParameterType.Date, "2026-03-01", out var date));
        Assert.Equal(new DateTime(2026, 3, 1), date);
    }

    [Theory]
    [InlineData(SqlParameterType.Int, "4x")]
    [InlineData(SqlParameterType.Decimal, "1,2,3")]
    [InlineData(SqlParameterType.Date, "not-a-date")]
    [InlineData(SqlParameterType.Guid, "123")]
    public void Rejects_invalid_values(SqlParameterType type, string raw) => Assert.False(ParameterBinder.TryConvert(type, raw, out _));

    [Theory]
    [InlineData("1abc")]
    [InlineData("a;drop")]
    [InlineData("a b")]
    [InlineData("")]
    public void Rejects_invalid_parameter_names(string name)
    {
        var ex = Assert.Throws<BusinessRuleException>(() => ParameterBinder.Bind([new QueryParameterDto(name, SqlParameterType.String, "x")], 10, 100));
        Assert.Equal(ErrorCodes.InvalidParameters, ex.ErrorCode);
    }

    [Fact]
    public void Rejects_duplicates_and_oversized_values()
    {
        Assert.Throws<BusinessRuleException>(() => ParameterBinder.Bind(
            [new QueryParameterDto("A", SqlParameterType.String, "x"), new QueryParameterDto("@a", SqlParameterType.String, "y")], 10, 100));
        Assert.Throws<BusinessRuleException>(() => ParameterBinder.Bind([new QueryParameterDto("A", SqlParameterType.String, new string('x', 101))], 10, 100));
        Assert.Throws<BusinessRuleException>(() => ParameterBinder.Bind(
            Enumerable.Range(0, 3).Select(i => new QueryParameterDto($"p{i}", SqlParameterType.Int, "1")).ToList(), 2, 100));
    }

    [Fact]
    public void Signature_is_order_independent_and_value_sensitive()
    {
        var a = ParameterBinder.Signature([new("A", SqlParameterType.Int, "1"), new("B", SqlParameterType.String, "x")]);
        var b = ParameterBinder.Signature([new("@b", SqlParameterType.String, "x"), new("a", SqlParameterType.Int, "1")]);
        var c = ParameterBinder.Signature([new("A", SqlParameterType.Int, "2"), new("B", SqlParameterType.String, "x")]);
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }
}
