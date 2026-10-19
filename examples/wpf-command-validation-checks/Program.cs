using System.ComponentModel;
using WpfCommandValidation;

var vm = new ThresholdViewModel();
int applyChanges = 0;
int cancelChanges = 0;
int committedChanges = 0;
vm.ApplyCommand.CanExecuteChanged += (_, _) => applyChanges++;
vm.CancelCommand.CanExecuteChanged += (_, _) => cancelChanges++;
vm.PropertyChanged += (_, e) =>
{
    if (e.PropertyName == nameof(vm.CurrentThreshold)) committedChanges++;
};

Check(!vm.ApplyCommand.CanExecute(null) && !vm.CancelCommand.CanExecute(null), "初始值不能重复应用或取消");
vm.ThresholdText = "abc";
Check(vm[nameof(vm.ThresholdText)].Length > 0 && vm.ErrorText.Length > 0, "格式错误可见");
Check(!vm.ApplyCommand.CanExecute(null) && vm.CancelCommand.CanExecute(null), "非法输入不可提交但可撤销");
vm.ApplyCommand.Execute(null);
Check(vm.CurrentThreshold == 50 && committedChanges == 0, "直接调用命令也不能绕过验证");

vm.ThresholdText = "";
Check(vm.ErrorText.Length > 0 && !vm.ApplyCommand.CanExecute(null), "空值不可提交");
vm.ThresholdText = "68.5";
Check(vm.ErrorText.Length > 0 && !vm.ApplyCommand.CanExecute(null), "小数不可提交");
vm.ThresholdText = " 68 ";
Check(vm.ErrorText.Length > 0 && !vm.ApplyCommand.CanExecute(null), "带空格的输入不可提交");
vm.ThresholdText = "101";
Check(vm.ErrorText.Contains("0–100") && !vm.ApplyCommand.CanExecute(null), "范围错误不可提交");
vm.ThresholdText = "50";
Check(!vm.ApplyCommand.CanExecute(null) && !vm.CancelCommand.CanExecute(null), "未改变的值不可重复提交");
vm.ThresholdText = "050";
Check(!vm.ApplyCommand.CanExecute(null) && vm.CancelCommand.CanExecute(null), "等值文本不重复应用，但可撤销格式变化");
vm.CancelCommand.Execute(null);
Check(vm.ThresholdText == "50", "取消把等值文本恢复为规范形式");

vm.ThresholdText = "68";
Check(vm.ErrorText.Length == 0 && vm.ApplyCommand.CanExecute(null), "合法变更可提交");
vm.ApplyCommand.Execute(null);
Check(vm.CurrentThreshold == 68 && committedChanges == 1, "提交只修改一次已应用值");
Check(!vm.ApplyCommand.CanExecute(null) && !vm.CancelCommand.CanExecute(null), "提交后命令关闭");
vm.ApplyCommand.Execute(null);
Check(committedChanges == 1, "直接重复调用也不重复提交");

vm.ThresholdText = "72";
vm.CancelCommand.Execute(null);
Check(vm.ThresholdText == "68" && vm.CurrentThreshold == 68, "取消仅撤销未提交文本");
Check(!vm.ApplyCommand.CanExecute(null) && !vm.CancelCommand.CanExecute(null), "取消后命令关闭");
Check(applyChanges > 0 && cancelChanges > 0, "输入变化发出 CanExecuteChanged");
Console.WriteLine("命令、输入验证、取消编辑和重复提交断言通过。");

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
