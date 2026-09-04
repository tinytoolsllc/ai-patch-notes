import { create } from "zustand";
import { persist } from "zustand/middleware";

type SortBy = "date" | "name";

interface FilterState {
  showPrerelease: boolean;
  sortBy: SortBy;
  groupByPackage: boolean;
  heroDismissed: boolean;
  setShowPrerelease: (show: boolean) => void;
  setSortBy: (sort: SortBy) => void;
  setGroupByPackage: (group: boolean) => void;
  togglePrerelease: () => void;
  toggleGroupByPackage: () => void;
  dismissHero: () => void;
}

export const useFilterStore = create<FilterState>()(
  persist(
    (set) => ({
      showPrerelease: true,
      sortBy: "date",
      groupByPackage: false,
      heroDismissed: false,
      setShowPrerelease: (show) => set({ showPrerelease: show }),
      setSortBy: (sort) => set({ sortBy: sort }),
      setGroupByPackage: (group) => set({ groupByPackage: group }),
      togglePrerelease: () => set((state) => ({ showPrerelease: !state.showPrerelease })),
      toggleGroupByPackage: () => set((state) => ({ groupByPackage: !state.groupByPackage })),
      dismissHero: () => set({ heroDismissed: true }),
    }),
    {
      name: "patchnotes-filters",
    },
  ),
);
