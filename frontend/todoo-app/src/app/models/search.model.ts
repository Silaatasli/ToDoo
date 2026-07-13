export interface GlobalSearchTeam {
  id: number;
  name: string;
}

export interface GlobalSearchBoard {
  id: number;
  name: string;
  teamId: number;
  teamName: string;
}

export interface GlobalSearchTask {
  id: number;
  title: string;
  teamId: number;
  teamName: string;
  boardId: number;
  boardColumnTitle: string;
}

export interface GlobalSearchPerson {
  id: number;
  email: string;
  displayName: string;
  hasProfilePhoto: boolean;
}

export interface GlobalSearchResult {
  teams: GlobalSearchTeam[];
  boards: GlobalSearchBoard[];
  tasks: GlobalSearchTask[];
  people: GlobalSearchPerson[];
}
