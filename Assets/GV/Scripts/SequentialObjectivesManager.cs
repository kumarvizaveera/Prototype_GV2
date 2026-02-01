using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VSX.UI;
using VSX.Objectives;

namespace VSX.VehicleCombatKits
{
    /// <summary>
    /// Manages the sequential activation of objectives and displays a progress counter.
    /// </summary>
    public class SequentialObjectivesManager : ObjectivesManager
    {
        [Header("Sequential Settings")]

        [Tooltip("The list of objectives to be completed in order. They will be activated sequentially.")]
        [SerializeField]
        protected List<ObjectiveController> objectivesSequence = new List<ObjectiveController>();

        [Tooltip("Text controller to display the '1/6' style progress.")]
        [SerializeField]
        protected TextController progressText;

        [Tooltip("Format string for progress (e.g. '{0}/{1}').")]
        [SerializeField]
        protected string progressFormat = "{0}/{1}";

        protected int currentObjectiveIndex = 0;


        protected override void Awake()
        {
            // Clear the base list so we don't duplicate or auto-find random things if configured otherwise
            objectiveControllers.Clear();

            // Disable all objectives except the first one
            for(int i = 0; i < objectivesSequence.Count; ++i)
            {
                if (objectivesSequence[i] != null)
                {
                    if (i == 0)
                    {
                        objectivesSequence[i].gameObject.SetActive(true);
                        objectiveControllers.Add(objectivesSequence[i]); // Add to base list specifically
                    }
                    else
                    {
                        objectivesSequence[i].gameObject.SetActive(false);
                    }
                }
            }

            // Let base Awake instantiate UI for the first objective
            base.Awake();

            // Subscribe to the first objective
            if (objectivesSequence.Count > 0 && objectivesSequence[0] != null)
            {
                objectivesSequence[0].onCompleted.AddListener(OnSequenceObjectiveCompleted);
            }

            UpdateProgressUI();
        }

        protected virtual void OnSequenceObjectiveCompleted()
        {
            // Unsubscribe from current
            if (currentObjectiveIndex < objectivesSequence.Count)
            {
                if (objectivesSequence[currentObjectiveIndex] != null)
                {
                    objectivesSequence[currentObjectiveIndex].onCompleted.RemoveListener(OnSequenceObjectiveCompleted);
                }
            }

            // Move to next
            currentObjectiveIndex++;
            UpdateProgressUI();

            if (currentObjectiveIndex < objectivesSequence.Count)
            {
                // Activate next
                ObjectiveController nextObjective = objectivesSequence[currentObjectiveIndex];
                if (nextObjective != null)
                {
                    nextObjective.gameObject.SetActive(true);
                    
                    // Add to base list and spawn UI logic
                    if (objectiveControllers.IndexOf(nextObjective) == -1)
                    {
                        objectiveControllers.Add(nextObjective);
                        
                        // Manually spawn UI since base.Awake() already ran
                        if (objectiveUIPrefab != null && objectiveUIParent != null)
                        {
                            ObjectiveUIController ui = Instantiate(objectiveUIPrefab, objectiveUIParent);
                            ui.SetObjective(nextObjective);
                            nextObjective.onCompleted.AddListener(OnObjectiveCompleted); // Hook into base events
                        }
                    }

                    // Subscribe to sequence logic
                    nextObjective.onCompleted.AddListener(OnSequenceObjectiveCompleted);
                }
            }
            else
            {
                // All done
                OnObjectivesCompleted();
            }
        }

        protected virtual void UpdateProgressUI()
        {
            if (progressText != null)
            {
                // Display Current / Total (e.g. 1/6). Note: Index is 0-based, so +1 for display.
                // If all completed, index might be equal to count.
                int displayIndex = Mathf.Min(currentObjectiveIndex + 1, objectivesSequence.Count);
                if (currentObjectiveIndex >= objectivesSequence.Count) displayIndex = objectivesSequence.Count; // Stick to max
                
                progressText.text = string.Format(progressFormat, displayIndex, objectivesSequence.Count);
            }
        }
    }
}
