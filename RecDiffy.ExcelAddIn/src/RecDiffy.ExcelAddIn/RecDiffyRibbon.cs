using System.Runtime.InteropServices;
using ExcelDna.Integration;
using ExcelDna.Integration.CustomUI;

namespace RecDiffy.ExcelAddIn;

[ComVisible(true)]
public sealed class RecDiffyRibbon : ExcelRibbon
{
    private const string CustomUi = """
        <customUI xmlns="http://schemas.microsoft.com/office/2009/07/customui" loadImage="LoadImage">
          <ribbon>
            <tabs>
              <tab id="RecDiffyTab" label="RecDiffy">
                <group id="ReconcileGroup" label="Reconcile" image="RecDiffy">
                  <button id="ReconcileButton"
                          label="Reconcile datasets"
                          size="large"
                          image="RecDiffy"
                          screentip="Reconcile datasets"
                          supertip="Open the RecDiffy window to compare a left and a right dataset."
                          onAction="OnReconcile" />
                </group>
              </tab>
            </tabs>
          </ribbon>
        </customUI>
        """;

    public override string GetCustomUI(string RibbonID) => CustomUi;

    public override object LoadImage(string imageId) =>
        imageId is RibbonIcons.RecDiffy ? RibbonIcons.ForThisDisplay() : base.LoadImage(imageId);

    public void OnReconcile(IRibbonControl control) => ExcelAsyncUtil.QueueAsMacro(OpenWindow);

    private static void OpenWindow() =>
        AddInServices.Window.ShowOrActivate(
            XlCall.TryExcel(XlCall.xlGetHwnd, out var handle) switch
            {
                XlCall.XlReturn.XlReturnSuccess => ExcelWindows.Resolve(handle),
                _ => 0,
            });
}
