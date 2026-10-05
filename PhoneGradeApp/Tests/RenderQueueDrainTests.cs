using Xunit;

namespace Tests;

/// <summary>
/// Proves the render queue is drained after every test, rather than after the
/// handful that remembered to ask for it.
///
/// A render pass that is still queued when a test ends is carried over to
/// whatever runs next, and it then runs during that next test's session setup
/// where the font manager is not registered yet. The result is a failure that
/// names a class which never touched rendering, and which class it lands on
/// depends on the order the run happened to take.
///
/// A count is asserted rather than a drain being re-done here, because a drain
/// inside this test would be the very thing being checked. It only works if the
/// assembly-level hook reaches every test, including the ones that end without
/// draining anything themselves.
/// </summary>
public class RenderQueueDrainTests
{
    [Fact]
    public void TheDrainRanAfterTheTestsThatRanBeforeThisOne()
    {
        Assert.True(DrainTheRenderQueueAfterEveryTestAttribute.Started > 0, "Before hook never ran");
        Assert.True(DrainTheRenderQueueAfterEveryTestAttribute.Drained > 0,
            "the assembly-level drain never ran, so any test can leave a render pass behind for the next one");
    }
}