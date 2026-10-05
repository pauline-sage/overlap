import { TestBed } from '@angular/core/testing';
import { AuthService } from '../../../auth.service';
import { QuizEditorComponent } from './quiz-editor';
describe('Custom quiz editor',()=>{
  it('edits one question at a time and saves a private quiz without sending an invitation',async()=>{
    const auth={submit:vi.fn().mockResolvedValue({id:9})};TestBed.configureTestingModule({imports:[QuizEditorComponent],providers:[{provide:AuthService,useValue:auth}]});
    const f=TestBed.createComponent(QuizEditorComponent);f.detectChanges();await f.whenStable();const c=f.componentInstance;
    expect(f.nativeElement.querySelectorAll('textarea')).toHaveLength(1);expect(c.ready()).toBe(false);c.title='Our evenings';
    c.questions=Array.from({length:5},(_,i)=>({prompt:'Our question '+i,choices:['One','Two','Three','Four']}));
    c.move(1);f.detectChanges();expect(f.nativeElement.textContent).toContain('Question 2 of 5');
    const saved=vi.fn();c.saved.subscribe(saved);await c.save();expect(auth.submit).toHaveBeenCalledExactlyOnceWith('quiz/templates',{title:'Our evenings',questions:c.questions,revision:null});expect(saved).toHaveBeenCalledOnce();
    c.questions[0].choices=['Same',' same ','Three','Four'];expect(c.ready()).toBe(false);await c.save();expect(auth.submit).toHaveBeenCalledOnce();f.destroy();
  });
});
