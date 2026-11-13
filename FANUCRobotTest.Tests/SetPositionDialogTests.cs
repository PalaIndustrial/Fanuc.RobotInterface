using FANUCRobotTest.UI;
using Fanuc.RobotInterface;

namespace FANUCRobotTest.Tests;

[TestClass]
public sealed class SetPositionDialogTests
{
    [STATestMethod]
    public void EditingDialogPositionDoesNotMutateSource()
    {
        var source = new Position
        {
            UserFrame = 2,
            UserTool = 3,
            Joint = new PositionJoint
            {
                J1 = 1, J2 = 2, J3 = 3, J4 = 4, J5 = 5, J6 = 6, J7 = 7, J8 = 8, J9 = 9
            },
            Cartisian = new PositionCartisian
            {
                X = 1, Y = 2, Z = 3, W = 4, P = 5, R = 6, E1 = 7, E2 = 8, E3 = 9,
                Config = CartisianConfig.FRUB, T4 = 10, T5 = 11, T6 = 12
            }
        };
        var dialog = new SetPositionDialog(source);
        var copy = dialog.Position;

        Assert.AreSame(copy, dialog.DataContext);
        Assert.AreNotSame(source, copy);
        Assert.AreNotSame(source.Joint, copy.Joint);
        Assert.AreNotSame(source.Cartisian, copy.Cartisian);
        Assert.IsTrue(source.Equals(copy));
        copy.Joint.J1 = 99;
        copy.Cartisian.X = 99;
        copy.UserFrame = 99;
        Assert.AreEqual(1f, source.Joint.J1);
        Assert.AreEqual(1f, source.Cartisian.X);
        Assert.AreEqual((short)2, source.UserFrame);
    }

    [STATestMethod]
    public void MissingPositionComponentsRemainMissing()
    {
        var dialog = new SetPositionDialog(new Position { UserTool = 3 });
        Assert.IsNull(dialog.Position.Joint);
        Assert.IsNull(dialog.Position.Cartisian);
        Assert.AreEqual((short)3, dialog.Position.UserTool);
    }
}
