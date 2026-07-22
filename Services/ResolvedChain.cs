using Mafi;
using Mafi.Core.Products;
using Mafi.Core.Prototypes;
using System.Collections.Generic;

namespace Iserik.FaFOptimiser.Services
{

    public class ChainNode
    {
        public bool IsFarm { get; set; }
        // --- NEW: Flag to safely identify Water/Salt without relying on missing icons ---
        public bool IsBaseResource { get; set; }
        public string MachineName { get; set; }
        public double MachineCount { get; set; }
        public ProductProto OutputProduct { get; set; }
        public Fix32 OutputAmount { get; set; }
        public IProtoWithIcon MachineProto { get; set; }

        public List<ChainNode> Inputs { get; set; } = new List<ChainNode>();

        public ChainNode Clone()
        {
            var clone = new ChainNode
            {
                IsFarm = this.IsFarm,
                IsBaseResource = this.IsBaseResource, // Copy the new flag
                MachineName = this.MachineName,
                MachineCount = this.MachineCount,
                OutputProduct = this.OutputProduct,
                MachineProto = this.MachineProto,
                OutputAmount = this.OutputAmount,
                Inputs = new List<ChainNode>()
            };

            foreach (var input in this.Inputs)
            {
                clone.Inputs.Add(input.Clone());
            }

            return clone;
        }
    }

    /// <summary>
    /// Represents the flattened, mathematical result of following a specific production chain.
    /// </summary>
    public class ResolvedChain
    {
        public ProductProto TargetProduct { get; set; }
        public Fix32 TargetAmount { get; set; }

        // The graphical tree structure
        public ChainNode RootNode { get; set; }

        // --- NEW: Temporary storage for accumulating inputs during cross-multiplication ---
        public List<ChainNode> PendingInputs { get; set; } = new List<ChainNode>();

        public double FarmsNeeded { get; set; }
        public Dictionary<ProductProto, Fix32> RawCropDemands { get; } = new Dictionary<ProductProto, Fix32>();
        public Dictionary<ProductProto, Fix32> NetByproducts { get; } = new Dictionary<ProductProto, Fix32>();
        public double ResourceScore { get; set; }

        public HashSet<ProductProto> SatisfiedProducts { get; } = new HashSet<ProductProto>();

        // Helper to add a product to this chain's "coverage"
        public void AddSatisfiedProduct(ProductProto product)
        {
            SatisfiedProducts.Add(product);
        }

        public bool IsEquivalentTo(ResolvedChain other)
        {
            if (other == null) return false;

            // 1. Compare Byproducts (using our existing 0.05f epsilon check)
            if (!AreDictionariesEqual(this.NetByproducts, other.NetByproducts)) return false;

            // 2. Compare the actual production path (ignoring base resources and farm tiers)
            return AreNodesEquivalent(this.RootNode, other.RootNode);
        }

        // --- KEEP THIS FOR THE OPTIMIZER ---
        public Dictionary<ProductProto, Fix32> GetRequiredCrops()
        {
            var crops = new Dictionary<ProductProto, Fix32>();
            ExtractCropsFromNode(this.RootNode, crops);
            return crops;
        }

        public Dictionary<ProductProto, Fix32> GetRequiredInputs()
        {
            var inputs = new Dictionary<ProductProto, Fix32>();
            ExtractInputsFromNode(this.RootNode, inputs);
            return inputs;
        }
        private void ExtractInputsFromNode(ChainNode node, Dictionary<ProductProto, Fix32> inputs)
        {
            if (node == null) return;

            if (node.IsFarm || node.IsBaseResource || node.Inputs.Count == 0)
            {
                if (inputs.ContainsKey(node.OutputProduct))
                    inputs[node.OutputProduct] += node.OutputAmount;
                else
                    inputs[node.OutputProduct] = node.OutputAmount;

                // Stop traversing if it's a farm to prevent pulling farm water into the factory inputs
                if (node.IsFarm) return;
            }

            foreach (var input in node.Inputs)
            {
                ExtractInputsFromNode(input, inputs);
            }
        }

        private void ExtractCropsFromNode(ChainNode node, Dictionary<ProductProto, Fix32> crops)
        {
            if (node == null) return;

            // If this node is a farm, its output is a crop! Add it to our total.
            if (node.IsFarm)
            {
                if (crops.ContainsKey(node.OutputProduct))
                    crops[node.OutputProduct] += node.OutputAmount;
                else
                    crops[node.OutputProduct] = node.OutputAmount;
            }

            // Recursively walk down the tree to find all other farms
            foreach (var input in node.Inputs)
            {
                ExtractCropsFromNode(input, crops);
            }
        }

        private bool AreNodesEquivalent(ChainNode nodeA, ChainNode nodeB)
        {
            if (nodeA == null && nodeB == null) return true;
            if (nodeA == null || nodeB == null) return false;

            // The products must match (e.g., both must be producing Corn)
            if (nodeA.OutputProduct != nodeB.OutputProduct) return false;

            // THE FIX: If both are farms, we consider them equivalent! 
            // We do not care if one is T2 and one is T3, or if they use different amounts of water.
            if (nodeA.IsFarm && nodeB.IsFarm) return true;

            // If they are factories, they must be the exact same machine type
            if (nodeA.MachineName != nodeB.MachineName) return false;

            // Filter out base resources (Water, Fertilizer) so we only compare the main ingredients
            // Filter out base resources safely without using LINQ
            var inputsA = new List<ChainNode>();
            foreach (var i in nodeA.Inputs)
            {
                if (!i.IsBaseResource) inputsA.Add(i);
            }
            inputsA.Sort((x, y) => (x.OutputProduct?.Id.Value ?? "").CompareTo(y.OutputProduct?.Id.Value ?? ""));

            var inputsB = new List<ChainNode>();
            foreach (var i in nodeB.Inputs)
            {
                if (!i.IsBaseResource) inputsB.Add(i);
            }
            inputsB.Sort((x, y) => (x.OutputProduct?.Id.Value ?? "").CompareTo(y.OutputProduct?.Id.Value ?? ""));

            if (inputsA.Count != inputsB.Count) return false;

            // Recursively check all branches going up the chain
            for (int i = 0; i < inputsA.Count; i++)
            {
                if (!AreNodesEquivalent(inputsA[i], inputsB[i])) return false;
            }

            return true;
        }

        private static bool AreDictionariesEqual(Dictionary<ProductProto, Fix32> dict1, Dictionary<ProductProto, Fix32> dict2)
        {
            if (dict1.Count != dict2.Count) return false;
            foreach (var kvp in dict1)
            {
                if (!dict2.TryGetValue(kvp.Key, out Fix32 val2)) return false;

                // Allow a tiny floating-point margin of error for math drift
                if (System.Math.Abs(kvp.Value.ToFloat() - val2.ToFloat()) > 0.05f) return false;
            }
            return true;
        }
    }
}