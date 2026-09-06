using System;
using System.Linq;
using Xunit;

namespace SimWorld.Tests.Common
{
    public class RangesAndCurvesTests
    {
        [Theory]
        [InlineData("1~3", 1, 3)]
        [InlineData(" 2 ~ 5 ", 2, 5)]
        [InlineData("7", 7, 7)]
        public void IntRange_parses_rimworld_notation(string text, int min, int max)
        {
            Assert.Equal(new IntRange(min, max), IntRange.FromString(text));
        }

        [Fact]
        public void IntRange_rejects_malformed_text()
        {
            Assert.Throws<FormatException>(() => IntRange.FromString("1~2~3"));
            Assert.Throws<FormatException>(() => IntRange.FromString("a~b"));
        }

        [Fact]
        public void IntRange_helpers()
        {
            var range = new IntRange(2, 6);
            Assert.Equal(4, range.Span);
            Assert.Equal(4f, range.Average);
            Assert.True(range.Includes(2));
            Assert.False(range.Includes(7));
            Assert.Equal(6, range.ClampToRange(9));
            Assert.Equal("2~6", range.ToString());
        }

        [Fact]
        public void FloatRange_lerps_and_inverse_lerps()
        {
            var range = new FloatRange(10f, 20f);
            Assert.Equal(15f, range.LerpThroughRange(0.5f));
            Assert.Equal(0.5f, range.InverseLerpThroughRange(15f));
            Assert.Equal(0f, range.InverseLerpThroughRange(5f));
            Assert.Equal(1f, range.InverseLerpThroughRange(25f));
            Assert.Equal(new FloatRange(0.5f, 2f), FloatRange.FromString("0.5~2"));
            Assert.Equal("0.5~2", new FloatRange(0.5f, 2f).ToString());
        }

        [Theory]
        [InlineData("(1, 2)", 1f, 2f)]
        [InlineData("(0.5,0.25)", 0.5f, 0.25f)]
        [InlineData("3, 4", 3f, 4f)]
        public void CurvePoint_parses_with_or_without_parentheses(string text, float x, float y)
        {
            Assert.Equal(new CurvePoint(x, y), CurvePoint.FromString(text));
        }

        [Fact]
        public void SimpleCurve_interpolates_and_clamps()
        {
            var curve = new SimpleCurve { new CurvePoint(0f, 0f), new CurvePoint(10f, 100f), new CurvePoint(20f, 0f) };
            Assert.Equal(50f, curve.Evaluate(5f));
            Assert.Equal(100f, curve.Evaluate(10f));
            Assert.Equal(50f, curve.Evaluate(15f));
            Assert.Equal(0f, curve.Evaluate(-5f));
            Assert.Equal(0f, curve.Evaluate(99f));
        }

        [Fact]
        public void SimpleCurve_sorts_points_assigned_out_of_order()
        {
            var curve = new SimpleCurve();
            curve.points.Add(new CurvePoint(10f, 1f));
            curve.points.Add(new CurvePoint(0f, 0f));
            Assert.Equal(0.5f, curve.Evaluate(5f));
            Assert.Equal(new[] { 0f, 10f }, curve.Select(p => p.x));
            Assert.Equal(0f, curve[0].x);
        }

        [Fact]
        public void SimpleCurve_with_no_points_evaluates_to_zero()
        {
            Assert.Equal(0f, new SimpleCurve().Evaluate(3f));
        }
    }
}
