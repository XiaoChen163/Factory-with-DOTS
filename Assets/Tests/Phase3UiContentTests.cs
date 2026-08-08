using NUnit.Framework;
using UnityEngine.UIElements;

namespace Factory.Tests
{
    public sealed class Phase3UiContentTests
    {
        [Test]
        public void ItemGrid_RendersSnapshotWithoutAccessingEcs()
        {
            VisualElement root = new();
            ItemSlotGridView view = new(root, null);
            view.Render(new[]
            {
                new ItemSlotSnapshot(0, new ItemId { Value = 2 }, 17, 100,
                    default, UiSlotAccess.InsertAndExtract),
                new ItemSlotSnapshot(1, default, 0, 100,
                    default, UiSlotAccess.InsertAndExtract)
            });

            Assert.That(root.childCount, Is.EqualTo(2));
            Assert.That(root[0].Q<Label>("count").text, Is.EqualTo("17"));
            Assert.That(root[1].Q<Label>("count").text, Is.Empty);
        }

        [Test]
        public void ItemGrid_ReusesSlotElementsWhenRevisionContentChanges()
        {
            VisualElement root = new();
            ItemSlotGridView view = new(root, null);
            view.Render(new[] { Slot(1) });
            VisualElement first = root[0];

            view.Render(new[] { Slot(9) });

            Assert.That(root[0], Is.SameAs(first));
            Assert.That(root[0].Q<Label>("count").text, Is.EqualTo("9"));
        }

        private static ItemSlotSnapshot Slot(ushort count) =>
            new(0, new ItemId { Value = 1 }, count, 100,
                default, UiSlotAccess.InsertAndExtract);
    }
}
