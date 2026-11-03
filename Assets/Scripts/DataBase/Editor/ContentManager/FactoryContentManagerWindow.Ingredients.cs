using System.Linq;
using UnityEditor;
using UnityEngine.UIElements;

public sealed partial class FactoryContentManagerWindow
{
    private string RecipeKey(int row) => draft.GetRecipes().Single(r => r.row == row).key;

    private void DrawIngredients(int recipeRow, bool output)
    {
        detail.Add(Heading(output ? "产物（槽位从 1 开始）" : "原料（允许为空）"));
        var items = draft.GetItems();
        var ingredients = draft.GetIngredients(RecipeKey(recipeRow), output);
        for (int i = 0; i < ingredients.Count; i++)
        {
            int index = i;
            var ingredient = ingredients[i];
            var item = items.FirstOrDefault(v => v.key == ingredient.itemKey);
            var row = Row();
            row.name = (output ? "output-" : "input-") + i;
            row.Add(new Label(output ? "槽位 " + (i + 1) : "原料 " + (i + 1)));
            var choose = new Button { name = (output ? "output-item-" : "input-item-") + i };
            choose.style.flexDirection = FlexDirection.Row;
            choose.style.flexGrow = 1;
            choose.Add(Icon(ItemIcon(item), 24));
            choose.Add(new Label(item == null ? ingredient.itemKey : item.name + " (" + item.key + ")"));
            choose.clicked += () => OpenItemPicker(choose, selected =>
            {
                draft.ReplaceIngredient(RecipeKey(recipeRow), output, ingredient.row, selected.key);
                IngredientsChanged();
            });
            row.Add(choose);
            var count = new IntegerField { name = (output ? "output-count-" : "input-count-") + i, value = ingredient.count };
            count.style.width = 65;
            Bind(row, count, value => draft.SetIngredientCount(RecipeKey(recipeRow), output, ingredient.row, value));
            if (output)
            {
                var up = new Button(() => Run(() => { draft.MoveOutput(RecipeKey(recipeRow), index, -1); IngredientsChanged(); })) { text = "↑", name = "output-up-" + i };
                up.SetEnabled(i > 0);
                row.Add(up);
                var down = new Button(() => Run(() => { draft.MoveOutput(RecipeKey(recipeRow), index, 1); IngredientsChanged(); })) { text = "↓", name = "output-down-" + i };
                down.SetEnabled(i + 1 < ingredients.Count);
                row.Add(down);
            }
            row.Add(new Button(() => Run(() => { draft.RemoveIngredient(RecipeKey(recipeRow), output, ingredient.row); IngredientsChanged(); })) { text = "移除" });
            detail.Add(row);
        }
        var add = new Button { text = output ? "添加产物" : "添加原料", name = output ? "output-add" : "input-add" };
        add.clicked += () => OpenItemPicker(add, item =>
        {
            draft.AddIngredient(RecipeKey(recipeRow), output, item.key);
            IngredientsChanged();
        });
        detail.Add(add);
    }

    private void OpenItemPicker(VisualElement anchor, System.Action<FactoryContentItem> select)
    {
        UnityEditor.PopupWindow.Show(anchor.worldBound, new FactoryItemSearchPopup(draft.GetItems(), ItemIcon, select));
    }

    private void IngredientsChanged() { RefreshList(); DrawDetail(); UpdateModified(); }
}
