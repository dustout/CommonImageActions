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
