namespace CommonImageActions.Core.Tests
{
    public class ImageActionsTests
    {
        [Fact]
        public void HasAnyActions_ShouldBeFalse_WhenNothingIsSet()
        {
            // Arrange
            var actions = new ImageActions();

            // Act & Assert
            Assert.False(actions.HasAnyActions());
        }

        [Fact]
        public void CopyConstructor_ShouldCopyEveryProperty()
        {
            // Arrange - give every property a value, whatever properties there are. A property
            // that is added later and forgotten in the copy constructor fails this test
            var original = new ImageActions();
            var properties = typeof(ImageActions).GetProperties().Where(x => x.CanRead && x.CanWrite).ToList();
            foreach (var property in properties)
            {
                property.SetValue(original, TestHelpers.CreateValue(property.PropertyType));
            }

            // Act
            var copy = new ImageActions(original);

            // Assert
            Assert.NotEmpty(properties);
            foreach (var property in properties)
            {
                var expected = property.GetValue(original);
                Assert.NotNull(expected);
                Assert.True(Equals(expected, property.GetValue(copy)), $"{property.Name} is not copied by the copy constructor.");
            }
        }

        [Theory]
        [InlineData(nameof(ImageActions.Quality))]
        [InlineData(nameof(ImageActions.Rotate))]
        [InlineData(nameof(ImageActions.FlipHorizontal))]
        [InlineData(nameof(ImageActions.FlipVertical))]
        [InlineData(nameof(ImageActions.BackgroundColor))]
        [InlineData(nameof(ImageActions.IsSepia))]
        [InlineData(nameof(ImageActions.IsInverted))]
        [InlineData(nameof(ImageActions.Brightness))]
        [InlineData(nameof(ImageActions.Contrast))]
        [InlineData(nameof(ImageActions.Saturation))]
        [InlineData(nameof(ImageActions.Blur))]
        [InlineData(nameof(ImageActions.Sharpen))]
        [InlineData(nameof(ImageActions.Pixelate))]
        [InlineData(nameof(ImageActions.Vignette))]
        [InlineData(nameof(ImageActions.WatermarkImage))]
        public void HasAnyActions_ShouldBeTrue_WhenOnlyThatPropertyIsSet(string propertyName)
        {
            // Arrange
            var actions = new ImageActions();
            var property = typeof(ImageActions).GetProperty(propertyName)!;
            property.SetValue(actions, TestHelpers.CreateValue(property.PropertyType));

            // Act & Assert
            Assert.True(actions.HasAnyActions());
        }

        [Fact]
        public void HasAnyActions_ShouldNeedAllFourCropValues()
        {
            var partial = new ImageActions() { CropX = 1, CropY = 1, CropWidth = 10 };
            var complete = new ImageActions() { CropX = 1, CropY = 1, CropWidth = 10, CropHeight = 10 };

            Assert.False(partial.HasAnyActions());
            Assert.True(complete.HasAnyActions());
        }

        [Fact]
        public void HasAnyActions_ShouldBeTrue_WhenOnlyGrayscaleIsSet()
        {
            // Arrange
            var actions = new ImageActions()
            {
                IsGrayscale = true
            };

            // Act & Assert
            Assert.True(actions.HasAnyActions());
        }

        [Fact]
        public void CopyConstructor_ShouldCopyGrayscale()
        {
            // Arrange
            var defaults = new ImageActions()
            {
                IsGrayscale = true
            };

            // Act
            var actions = new ImageActions(defaults);

            // Assert
            Assert.True(actions.IsGrayscale);
        }
    }
}
