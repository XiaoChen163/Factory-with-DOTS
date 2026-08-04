using System.Collections.Generic;
using NUnit.Framework;
using Unity.Entities;

namespace Factory.Tests
{
    public static class TransportAssertions
    {
        public static HashSet<Entity> CaptureItems(
            TransportStateSnapshot snapshot)
        {
            HashSet<Entity> items = new HashSet<Entity>();
            for (int i = 0; i < snapshot.Nodes.Length; i++)
            {
                Entity item = snapshot.Nodes[i].CurrentItem;
                if (item != Entity.Null)
                {
                    Assert.That(
                        items.Add(item),
                        Is.True,
                        $"Item {item} is referenced by more than one " +
                        "transport node.");
                }
            }

            return items;
        }

        public static void AssertItemSetPreserved(
            TransportStateSnapshot before,
            TransportStateSnapshot after)
        {
            HashSet<Entity> beforeItems = CaptureItems(before);
            HashSet<Entity> afterItems = CaptureItems(after);
            Assert.That(
                afterItems.SetEquals(beforeItems),
                Is.True,
                "The transport tick lost, duplicated, or replaced an item.");
        }

        public static void AssertEquivalent(
            TransportStateSnapshot expected,
            TransportStateSnapshot actual,
            float progressTolerance = 0.00001f)
        {
            Assert.That(actual.Nodes.Length, Is.EqualTo(expected.Nodes.Length));
            for (int i = 0; i < expected.Nodes.Length; i++)
            {
                TransportNodeSnapshot expectedNode = expected.Nodes[i];
                TransportNodeSnapshot actualNode = actual.Nodes[i];
                Assert.That(actualNode.Kind, Is.EqualTo(expectedNode.Kind));
                Assert.That(actualNode.Cell, Is.EqualTo(expectedNode.Cell));
                Assert.That(
                    actualNode.CurrentItem,
                    Is.EqualTo(expectedNode.CurrentItem));
                Assert.That(
                    actualNode.Progress,
                    Is.EqualTo(expectedNode.Progress)
                        .Within(progressTolerance));
                Assert.That(
                    actualNode.Cursor,
                    Is.EqualTo(expectedNode.Cursor));
            }
        }
    }
}
