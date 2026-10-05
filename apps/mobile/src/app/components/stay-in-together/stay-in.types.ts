export type StayInActivity='menu'|'watch'|'read'|'quiz'|'cooking';
export interface StayInRequest {activity:StayInActivity;entryId?:number;contestId?:number;partnerPicks?:boolean;}
